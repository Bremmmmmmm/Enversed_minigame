using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Animation_player : MonoBehaviour
{
    public GameObject Camera;
    public GameObject Screen;
    public GameObject LoadingBar;
    public GameObject Puzzle;

    private AudioSource scannersound;
    // Start is called before the first frame update
    void Start()
    {
        scannersound = GetComponent<AudioSource>();
        //scene: 
        //level3 = 3.1
        //level2 = 2.78
        StartCoroutine(StartTimer(2.78f));

    }

    IEnumerator StartTimer(float duration)
    {
        yield return new WaitForSeconds(duration);
            print("clicked");
            StartAnimation();
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
        Animator animatorScreen = Screen.GetComponent<Animator>();
        animatorScreen.SetBool("FadeIn", true);
        StartCoroutine(WaitAndActivateLoadingBar());
    }

    private IEnumerator WaitAndActivateLoadingBar()
    {
        // Wait for 4 seconds
        yield return new WaitForSeconds(4);

        // Set the LoadingBar animation bool
        Animator animatorLoadingBar = LoadingBar.GetComponent<Animator>();
        animatorLoadingBar.SetBool("Active", true);
        StartCoroutine(WaitAndContinueAnimation());
    }
    
    private IEnumerator WaitAndContinueAnimation()
    {
        // Wait for 4 seconds
        yield return new WaitForSeconds(4);

        Animator animatorScreen = Screen.GetComponent<Animator>();
        animatorScreen.SetBool("FadeIn", false);
        animatorScreen.SetBool("FadeOut", true);
        StartCoroutine(WaitAndLoadPuzzle());
    }
    
    private IEnumerator WaitAndLoadPuzzle()
    {
        // Wait for 4 seconds
        yield return new WaitForSeconds(4);
        
        Animator animatorCamera = Camera.GetComponent<Animator>();
        animatorCamera.SetTrigger("start_camera_zoom");
        Animator animatorPuzzle = Puzzle.GetComponent<Animator>();
        animatorPuzzle.SetBool("FadeIn", true);
    }
}
