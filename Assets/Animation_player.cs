using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Animation_player : MonoBehaviour
{
    public GameObject Camera;
    public GameObject Screen;
    private bool started = false;

    private AudioSource scannersound;
    // Start is called before the first frame update
    void Start()
    {
        scannersound = GetComponent<AudioSource>();
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && !started)
        {
            print("clicked");
            started = true;
            StartAnimation();
        }
    }

    void StartAnimation()
    {
        Animator animatorscanner = GetComponent<Animator>();

        animatorscanner.SetTrigger("start_scanner");
        scannersound.Play();
    }

    void ContinueAnimation()
    {
        scannersound.Stop();

        Animator animatorCamera = Camera.GetComponent<Animator>();
        animatorCamera.SetTrigger("start_camera_movement");
        
    }
}
