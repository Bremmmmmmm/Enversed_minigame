using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    public Animator MoveCamera;

    public void MoveCameraToPosition(string position)
    {
        MoveCamera.SetTrigger("start_camera_movement");
    }
}
