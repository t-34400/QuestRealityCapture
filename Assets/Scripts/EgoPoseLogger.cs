using System.IO;
using System.Text;
using UnityEngine;

public class EgoPoseLogger : MonoBehaviour
{
    [Header("Hand Tracking Refs")]
    public OVRSkeleton leftSkeleton;
    public OVRSkeleton rightSkeleton;
    public OVRHand leftHand;   // Needed for Pinch Strength
    public OVRHand rightHand;  // Needed for Pinch Strength

    [Header("Body Tracking Ref")]
    public OVRBody bodyTracker; // Drag the object with OVRBody script here

    [Header("Output Settings")]
    [SerializeField] private string directoryName = "";

    private StreamWriter writerLeft;
    private StreamWriter writerRight;
    private StreamWriter writerBody;
    private bool isRecording = false;

    public string DirectoryName
    {
        get => directoryName;
        set => directoryName = value;
    }

    public void StartLogging()
    {
        StopLogging();

        string folderPath = Path.Combine(Application.persistentDataPath, directoryName);
        Directory.CreateDirectory(folderPath);

        // Initialize CSV writers (64kb buffer)
        writerLeft = new StreamWriter(Path.Combine(folderPath, "left_hand_pose.csv"), false, Encoding.UTF8, 65536);
        writerRight = new StreamWriter(Path.Combine(folderPath, "right_hand_pose.csv"), false, Encoding.UTF8, 65536);
        writerBody = new StreamWriter(Path.Combine(folderPath, "body_pose.csv"), false, Encoding.UTF8, 65536);

        // Write Headers
        string handHeader = "UnixTime,UnityTime,PinchStrength,IsGrabbing," + BuildHandHeader();
        writerLeft.WriteLine(handHeader);
        writerRight.WriteLine(handHeader);

        string bodyHeader = "UnixTime,UnityTime," + BuildBodyHeader();
        writerBody.WriteLine(bodyHeader);

        isRecording = true;
    }

    public void StopLogging()
    {
        isRecording = false;
        writerLeft?.Close();
        writerRight?.Close();
        writerBody?.Close();
        writerLeft = null;
        writerRight = null;
        writerBody = null;
    }

    void Update()
    {
        if (!isRecording) return;

        float t = Time.time;
        long unix = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // --- Log Left Hand ---
        if (leftSkeleton != null && leftSkeleton.IsInitialized)
        {
            float pinch = leftHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            int isGrabbing = pinch > 0.8f ? 1 : 0; 
            WriteHandData(writerLeft, leftSkeleton, t, unix, pinch, isGrabbing);
        }

        // --- Log Right Hand ---
        if (rightSkeleton != null && rightSkeleton.IsInitialized)
        {
            float pinch = rightHand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            int isGrabbing = pinch > 0.8f ? 1 : 0;
            WriteHandData(writerRight, rightSkeleton, t, unix, pinch, isGrabbing);
        }

        // --- Log Body ---
        if (bodyTracker != null && bodyTracker.BodyState.HasValue)
        {
            WriteBodyData(writerBody, bodyTracker, t, unix);
        }
    }

    void WriteHandData(StreamWriter writer, OVRSkeleton skel, float t, long unix, float pinch, int grabbing)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"{unix},{t:F4},{pinch:F3},{grabbing}");

        foreach (var bone in skel.Bones)
        {
            Vector3 p = bone.Transform.position;
            Quaternion r = bone.Transform.rotation;
            sb.Append($",{p.x:F4},{p.y:F4},{p.z:F4},{r.x:F4},{r.y:F4},{r.z:F4},{r.w:F4}");
        }
        writer.WriteLine(sb.ToString());
    }

    void WriteBodyData(StreamWriter writer, OVRBody body, float t, long unix)
    {
        // Ensure we actually have data before trying to read it
        if (!body.BodyState.HasValue) return;

        var skeleton = body.BodyState.Value; 

        StringBuilder sb = new StringBuilder();
        sb.Append($"{unix},{t:F4}");

        // Loop through the JointLocations array
        for (int i = 0; i < skeleton.JointLocations.Length; i++)
        {
            var joint = skeleton.JointLocations[i];
            
            // Check if this joint is valid
            if (!joint.OrientationValid || !joint.PositionValid)
            {
                sb.Append(",0,0,0,0,0,0,0"); 
                continue;
            }

            // --- THE FIX IS HERE ---
            // We convert the internal OVR types to Unity types using these extensions
            Vector3 p = joint.Pose.Position.FromFlippedZVector3f();
            Quaternion r = joint.Pose.Orientation.FromFlippedZQuatf();
            
            // Format: Px, Py, Pz, Rx, Ry, Rz, Rw
            sb.Append($",{p.x:F4},{p.y:F4},{p.z:F4},{r.x:F4},{r.y:F4},{r.z:F4},{r.w:F4}");
        }
        writer.WriteLine(sb.ToString());
    }

    string BuildHandHeader()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 24; i++)
        {
            sb.Append($"HandBone{i}_Px,HandBone{i}_Py,HandBone{i}_Pz,HandBone{i}_Rx,HandBone{i}_Ry,HandBone{i}_Rz,HandBone{i}_Rw,");
        }
        return sb.ToString().TrimEnd(',');
    }

    string BuildBodyHeader()
    {
        // OVRBody generally tracks ~70 joints. We create dynamic headers for them.
        StringBuilder sb = new StringBuilder();
        // 84 is a safe upper limit for current OVRBody joint counts (Body_End is usually ~82)
        for (int i = 0; i < 84; i++) 
        {
            sb.Append($"BodyJoint{i}_Px,BodyJoint{i}_Py,BodyJoint{i}_Pz,BodyJoint{i}_Rx,BodyJoint{i}_Ry,BodyJoint{i}_Rz,BodyJoint{i}_Rw,");
        }
        return sb.ToString().TrimEnd(',');
    }

    void OnDestroy()
    {
        StopLogging();
    }
}