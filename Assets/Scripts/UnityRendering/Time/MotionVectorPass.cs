using UnityEngine;

public class MotionVectorSettings
{
    public Vector3 mapCenterPosition;
    public Vector3 mapCenterScale;
    public Vector3 ccpPosition;
    public Quaternion ccpRotation;

    public Matrix4x4 prevProjViewMulInvModel;
    public Matrix4x4 currProjView;
    public Matrix4x4 prevProjView;
    public Matrix4x4 currInvProjView;
    public Matrix4x4 carPrevProjViewMulInvModel;

    private Vector3 _lastPosition;
    private Vector3 _lastScale;
    private Vector3 _lastCcpPosition;
    private Quaternion _lastCcpRotation;

    public void Update(Camera camera)
    {
        var proj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        var view = camera.worldToCameraMatrix;

        currProjView = proj * view;
        currInvProjView = currProjView.inverse;

        Matrix4x4 invModelMatrix = AdditionalMatrix(ref _lastPosition, ref mapCenterPosition, ref _lastScale, ref mapCenterScale);
        prevProjViewMulInvModel = prevProjView * invModelMatrix;

        Matrix4x4 invTranslate = Matrix4x4.identity;
        Matrix4x4 translate = Matrix4x4.identity;
        if (!IsEqual(_lastCcpPosition, ccpPosition))
        {
            translate = Matrix4x4.Translate(_lastCcpPosition);
            invTranslate = Matrix4x4.Translate(-ccpPosition);
            _lastCcpPosition = ccpPosition;
        }

        Matrix4x4 invRotate = Matrix4x4.identity;
        Matrix4x4 rotate = Matrix4x4.identity;
        if (!IsEqual(_lastCcpRotation, ccpRotation))
        {
            rotate = Matrix4x4.Rotate(_lastCcpRotation);
            invRotate = Matrix4x4.Rotate(ccpRotation).inverse;
            _lastCcpRotation = ccpRotation;
        }

        carPrevProjViewMulInvModel = prevProjView * translate * rotate * invRotate * invTranslate;
        prevProjView = currProjView;
    }

    private Matrix4x4 AdditionalMatrix(ref Vector3 lastPos, ref Vector3 currPos, ref Vector3 lastScale, ref Vector3 currScale)
    {
        Matrix4x4 invTranslate = Matrix4x4.identity;
        Matrix4x4 translate = Matrix4x4.identity;
        if (!IsEqual(lastPos, currPos))
        {
            translate = Matrix4x4.Translate(lastPos);
            invTranslate = Matrix4x4.Translate(-currPos);
            lastPos = currPos;
        }

        Matrix4x4 scale = Matrix4x4.identity;
        if (!IsEqual(lastScale, currScale) && currScale != Vector3.zero)
        {
            var deltaScale = new Vector3(lastScale.x / currScale.x, lastScale.y / currScale.y, lastScale.z / currScale.z);
            scale = Matrix4x4.Scale(deltaScale);
            lastScale = currScale;
        }

        return translate * scale * invTranslate;
    }

    private bool IsEqual(in Vector3 a, in Vector3 b) => Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) && Mathf.Approximately(a.z, b.z);
    private bool IsEqual(in Quaternion a, in Quaternion b) => Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y) && Mathf.Approximately(a.z, b.z) && Mathf.Approximately(a.w, b.w);
}