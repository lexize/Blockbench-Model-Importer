using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.EditorTools;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
public class BBModelImporter : EditorWindow
{
    public string file = "";
    public Material modelMaterial;
    public bool use_anims;

    [MenuItem("BlockBench utils/Load .bbmodel asset")]
    public static void OnClick()
    {
        BBModelImporter window = EditorWindow.GetWindow<BBModelImporter>();
        window.Show();
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Select file"))
        {
            file = EditorUtility.OpenFilePanelWithFilters("Import BBmodel", "C:/", new string[] { "BlockBench model file", "bbmodel" });
        }
        
        EditorGUILayout.LabelField($"Current file path: {file}");
        modelMaterial = (Material)EditorGUILayout.ObjectField("Model material", modelMaterial, typeof(Material), true);
        use_anims = EditorGUILayout.Toggle("Load included animations", use_anims);
        if (file != "" && GUILayout.Button("Generate model"))
        {
            string json_string = File.ReadAllText(file);
            JObject jObject = JObject.Parse(json_string);
            BBFile bbFile = jObject.ToObject<BBFile>();
        
            Dictionary<string, GameObject> cubes = new Dictionary<string, GameObject>();
            if (Directory.Exists($"Assets/BlockBenchExport/Meshes/{bbFile.name}"))
            {
                string[] filenames = Directory.GetFiles($"Assets/BlockBenchExport/Meshes/{bbFile.name}");
                foreach (var item in filenames)
                {
                    File.Delete(item);
                }
            }
            foreach (Cube cube in bbFile.elements)
            {
                GameObject cb = GenerateGameObjectByCube(cube, bbFile, modelMaterial);
                cubes.Add(cube.uuid, cb);
            }


            GameObject mgo = new GameObject(bbFile.name);
            if (UnityEditor.Selection.activeGameObject != null) mgo.transform.parent = UnityEditor.Selection.activeGameObject.transform;
        
            var bones = IterateOverGroup(mgo, bbFile.outliner, cubes);
            
            if (use_anims)
            {
                foreach (var item in bbFile.animations)
                {
                    Directory.CreateDirectory($"Assets/BlockBenchExport/Animations/{bbFile.name}");
                    AssetDatabase.CreateAsset(GenerateClipFromAnim(item, 60, mgo, bones), $"Assets/BlockBenchExport/Animations/{bbFile.name}/{item.name.Replace(".", "-")}.anim");
                }

            }
        }
    }

    public static Dictionary<string, GameObject> IterateOverGroup(GameObject parent, JArray objects, Dictionary<string, GameObject> cubes, Dictionary<string, GameObject> inputGroupObjs = null)
    {
        if (inputGroupObjs == null)
        {
            inputGroupObjs = new Dictionary<string, GameObject>();
        }
        foreach (var item in objects.Children())
        {
            if (item.Type == JTokenType.String)
            {
                cubes[item.ToString()].transform.parent = parent.transform;
            }
            else
            {
                JObject jobject = item.ToObject<JObject>();
                string name = jobject["name"].ToString();
                float[] origin = jobject["origin"].ToObject<float[]>();
                float[] rotation;
                if (jobject.ContainsKey("rotation")) rotation = jobject["rotation"].ToObject<float[]>();
                else rotation = new float[] { 0, 0, 0 };
                JArray objs = jobject["children"].ToObject<JArray>();
                GameObject groupObject = new GameObject(name);
                groupObject.transform.parent = parent.transform;
                groupObject.transform.position = new Vector3(origin[0]/16, origin[1]/16, origin[2]/16);
                inputGroupObjs.Add(item["uuid"].ToString(), groupObject);
                IterateOverGroup(groupObject, objs, cubes, inputGroupObjs);
                groupObject.transform.rotation = Quaternion.Euler(rotation[0], rotation[1], rotation[2]);
            }
        }
        return inputGroupObjs;
    }

    public static AnimationClip GenerateClipFromAnim(Anim input, int framerate, GameObject rootGroup, Dictionary<string, GameObject> groups)
    {
        AnimationClip clip = new AnimationClip();
        clip.name = input.name.Replace(".", " ");
        clip.frameRate = framerate;
        foreach (var item in input.animators.Keys)
        {
            BBAnimator animator = input.animators[item];
            Dictionary<string, List<Keyframe>> rotationKeyframes = new Dictionary<string, List<Keyframe>>();
            rotationKeyframes.Add("x", new List<Keyframe>());
            rotationKeyframes.Add("y", new List<Keyframe>());
            rotationKeyframes.Add("z", new List<Keyframe>());

            Dictionary<string, List<Keyframe>> positionKeyframes = new Dictionary<string, List<Keyframe>>();
            positionKeyframes.Add("x", new List<Keyframe>());
            positionKeyframes.Add("y", new List<Keyframe>());
            positionKeyframes.Add("z", new List<Keyframe>());

            foreach (var kf in animator.keyframes)
            {
                if (kf.channel == "rotation")
                {
                    rotationKeyframes["x"].Add(new Keyframe(kf.time, kf.data_points[0]["x"]));
                    rotationKeyframes["y"].Add(new Keyframe(kf.time, kf.data_points[0]["y"]));
                    rotationKeyframes["z"].Add(new Keyframe(kf.time, kf.data_points[0]["z"]));
                }
                else if (kf.channel == "position")
                {
                    positionKeyframes["x"].Add(new Keyframe(kf.time, groups[item].transform.localPosition.x + (kf.data_points[0]["x"] / 16)));
                    positionKeyframes["y"].Add(new Keyframe(kf.time, groups[item].transform.localPosition.y + (kf.data_points[0]["y"] / 16)));
                    positionKeyframes["z"].Add(new Keyframe(kf.time, groups[item].transform.localPosition.z + (kf.data_points[0]["z"] / 16)));
                }
            }
            AnimationCurve rot_x_curve = new AnimationCurve(rotationKeyframes["x"].ToArray());
            AnimationCurve rot_y_curve = new AnimationCurve(rotationKeyframes["y"].ToArray());
            AnimationCurve rot_z_curve = new AnimationCurve(rotationKeyframes["z"].ToArray());
            AnimationCurve pos_x_curve = new AnimationCurve(positionKeyframes["x"].ToArray());
            AnimationCurve pos_y_curve = new AnimationCurve(positionKeyframes["y"].ToArray());
            AnimationCurve pos_z_curve = new AnimationCurve(positionKeyframes["z"].ToArray());
            clip.SetCurve(AnimationUtility.CalculateTransformPath(groups[item].transform, rootGroup.transform), typeof(Transform), "localEulerAnglesRaw.x", rot_x_curve);
            clip.SetCurve(AnimationUtility.CalculateTransformPath(groups[item].transform, rootGroup.transform), typeof(Transform), "localEulerAnglesRaw.y", rot_y_curve);
            clip.SetCurve(AnimationUtility.CalculateTransformPath(groups[item].transform, rootGroup.transform), typeof(Transform), "localEulerAnglesRaw.z", rot_z_curve);
            clip.SetCurve(AnimationUtility.CalculateTransformPath(groups[item].transform, rootGroup.transform), typeof(Transform), "m_LocalPosition.x", pos_x_curve);
            clip.SetCurve(AnimationUtility.CalculateTransformPath(groups[item].transform, rootGroup.transform), typeof(Transform), "m_LocalPosition.y", pos_y_curve);
            clip.SetCurve(AnimationUtility.CalculateTransformPath(groups[item].transform, rootGroup.transform), typeof(Transform), "m_LocalPosition.z", pos_z_curve);
        }
        return clip;
    }

    public static GameObject GenerateGameObjectByCube(Cube inCube, BBFile inBBfile, Material modelMaterial)
    {
        int uv_widht = inBBfile.resolution.width;
        int uv_height = inBBfile.resolution.height;
        Mesh cubeMesh = new Mesh();
        float[] x_uv_values = { inCube.from[0], inCube.to[0] };
        float[] y_uv_values = { inCube.from[1], inCube.to[1] };
        float[] z_uv_values = { inCube.from[2], inCube.to[2] };
        Vector3 pivot = new Vector3(inCube.from[0] + inCube.to[0], inCube.from[1] + inCube.to[1], inCube.from[2] + inCube.to[2]) / 32;
        List<int> tris = new List<int>();
        List<Vector3> verts = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        int cVert = 0;
        for (int i = 0; i < 6; i++)
        {
            float[] cuvs = new float[4];
            switch (i)
            {
                case 0:
                    cuvs = inCube.faces.north.uv;
                    break;
                case 1:
                    cuvs = inCube.faces.south.uv;
                    break;
                case 2:
                    cuvs = inCube.faces.up.uv;
                    break;
                case 3:
                    cuvs = inCube.faces.down.uv;
                    break;
                case 4:
                    cuvs = inCube.faces.east.uv;
                    break;
                case 5:
                    cuvs = inCube.faces.west.uv;
                    break;
            }
            for (int j = 0; j < 6; j++)
            {
                Vector3 cVec = VoxelData.voxelVerts[VoxelData.voxelTris[i, j]];
                verts.Add(
                        new Vector3(
                            Mathf.Lerp(inCube.from[0], inCube.to[0], cVec.x)/16,
                            Mathf.Lerp(inCube.from[1], inCube.to[1], cVec.y)/16,
                            Mathf.Lerp(inCube.from[2], inCube.to[2], cVec.z)/16
                            ) - pivot
                    );
                tris.Add(cVert);
                cVert++;
            }
            foreach (var item in VoxelData.voxelUvs)
            {
                uvs.Add(
                    new Vector2(
                        Mathf.Lerp(cuvs[0], cuvs[2], item.x) / uv_widht,
                        Mathf.Lerp(uv_height - cuvs[3], uv_height - cuvs[1], item.y) / uv_height
                        )
                    );
            }

        }

        cubeMesh.vertices = verts.ToArray();
        cubeMesh.triangles = tris.ToArray();
        cubeMesh.uv = uvs.ToArray();
        cubeMesh.RecalculateNormals();
        cubeMesh.RecalculateBounds();
        cubeMesh.RecalculateTangents();
        
        if(!Directory.Exists("Assets/BlockBenchExport")) AssetDatabase.CreateFolder("Assets","BlockBenchExport");
        if (!Directory.Exists("Assets/BlockBenchExport/Meshes")) AssetDatabase.CreateFolder("Assets/BlockBenchExport", "Meshes");
        if (!Directory.Exists($"Assets/BlockBenchExport/Meshes/{inBBfile.name}")) AssetDatabase.CreateFolder("Assets/BlockBenchExport/Meshes", inBBfile.name);
        int cubesCount = Directory.GetFiles($"Assets/BlockBenchExport/Meshes/{inBBfile.name}", "*.asset").Length;
        AssetDatabase.CreateAsset(cubeMesh,$"Assets/BlockBenchExport/Meshes/{inBBfile.name}/{cubesCount}.asset");
        GameObject go = new GameObject(inCube.name);
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        if (modelMaterial != null) mr.material = modelMaterial;
        MeshFilter mf = go.AddComponent<MeshFilter>();
        go.transform.localPosition = pivot;
        mf.mesh = cubeMesh;
        return go;
    }

    [Serializable]
    public class BBFile
    {
        public ResolutionStruct resolution;
        public Cube[] elements;
        public JArray outliner;
        public string name;
        public Anim[] animations;
    }

    public class Anim
    {
        public float length;
        public string name;
        public Dictionary<string, BBAnimator> animators;
    }

    public class BBAnimator
    {
        public BBKeyframe[] keyframes;
    }

    public class BBKeyframe
    {
        public string channel;
        public Dictionary<string, float>[] data_points;
        public float time;
        public string interpolation;
    }

    [Serializable]
    public struct ResolutionStruct
    {
        public int width;
        public int height;
    }

    [Serializable]
    public struct FacesStruct
    {
        public FaceStruct north;
        public FaceStruct east;
        public FaceStruct south;
        public FaceStruct west;
        public FaceStruct up;
        public FaceStruct down;
    }

    [Serializable]
    public struct FaceStruct
    {
        public float[] uv;
    }

    [Serializable]
    public class Cube
    {
        public string name;
        public float[] from;
        public float[] to;
        public FacesStruct faces;
        public string uuid;
    }
    [Serializable]
    public class Group
    {
        public string name;
        public float[] origin;
        public string uuid;
        public JArray children;
    }

    public class GroupConverter : JsonConverter<Group>
    {
        public override Group ReadJson(JsonReader reader, Type objectType, Group existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (objectType == "".GetType())
            {
                return null;
            }
            var token = JToken.Load(reader);
            return token.ToObject<Group>(serializer);
        }

        public override void WriteJson(JsonWriter writer, Group value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanWrite => false;
    }

    
}

// Code was taken from https://github.com/b3agz/Code-A-Game-Like-Minecraft-In-Unity/blob/master/01-the-first-voxel/Assets/Scripts/VoxelData.cs
public static class VoxelData
{


    public static readonly Vector3[] voxelVerts = new Vector3[8] {

        new Vector3(0.0f, 0.0f, 0.0f),
        new Vector3(1.0f, 0.0f, 0.0f),
        new Vector3(1.0f, 1.0f, 0.0f),
        new Vector3(0.0f, 1.0f, 0.0f),
        new Vector3(0.0f, 0.0f, 1.0f),
        new Vector3(1.0f, 0.0f, 1.0f),
        new Vector3(1.0f, 1.0f, 1.0f),
        new Vector3(0.0f, 1.0f, 1.0f),

    };

    public static readonly int[,] voxelTris = new int[6, 6] {

        {0, 3, 1, 1, 3, 2}, // Back Face
		{5, 6, 4, 4, 6, 7}, // Front Face
		{3, 7, 2, 2, 7, 6}, // Top Face
		{1, 5, 0, 0, 5, 4}, // Bottom Face
		{4, 7, 0, 0, 7, 3}, // Left Face
		{1, 2, 5, 5, 2, 6} // Right Face

	};

    public static readonly Vector2[] voxelUvs = new Vector2[6] {
        new Vector2 (0.0f, 0.0f),
        new Vector2 (0.0f, 1.0f),
        new Vector2 (1.0f, 0.0f),
        new Vector2 (1.0f, 0.0f),
        new Vector2 (0.0f, 1.0f),
        new Vector2 (1.0f, 1.0f)
    };


}