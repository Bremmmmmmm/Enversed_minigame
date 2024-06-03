using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class PuzzlePieceController : MonoBehaviour
{
    //WINCONDITION
    public int winplace;

    private Vector3 screenPoint;
    private Vector3 offset;
    public Vector3 centeroffset; 
    private bool isDragging = false;
    public List<GameObject> pathPoints = new List<GameObject>();
    public List<GameObject> lines = new List<GameObject>();
    public int currentTargetIndex = 0;
    public int previousTargetIndex = 0;
    private Vector3 cursorPoint;
    private Vector3 cursorPosition;
    private SpriteRenderer spriteRenderer;
    private bool moving = false;
    public Vector3 startposition;
    public Vector3 targetposition;
    private Rigidbody2D rb2d;
    private Vector3 a = new Vector3(0.1f,0.1f,0.1f);
    private Vector3 b = new Vector3(-0.1f,-0.1f,-0.1f);
    public bool enabled = true;

    void Start()
    {
        rb2d = GetComponent<Rigidbody2D>();
        transform.position = pathPoints[0].transform.position + centeroffset;
        spriteRenderer = GetComponent<SpriteRenderer>();
        
        List<GameObject> availablePathPoints1 = pathPoints[currentTargetIndex].GetComponent<node>().connections;
        availablePathPoints1.Add(pathPoints[currentTargetIndex]);
        string names = "";
        foreach (GameObject point in availablePathPoints1)
        {
            names += point.name + " ";
        }
        print("Available points: " + names);
    }

    void OnMouseDown()
    {
        if(enabled == false)
        {
            return;
        }

        screenPoint = Camera.main.WorldToScreenPoint(gameObject.transform.position);
        offset = gameObject.transform.position - Camera.main.ScreenToWorldPoint(new Vector3(Input.mousePosition.x, Input.mousePosition.y, screenPoint.z));
        spriteRenderer.color = Color.yellow;
        isDragging = true;
    }

    void Update()
    {
        if(enabled == false)
        {
            return;
        }


        if (!moving)
        {
            rb2d.constraints = RigidbodyConstraints2D.FreezePosition | RigidbodyConstraints2D.FreezeRotation;
            startposition = transform.position;
            //transform.position = pathPoints[0].transform.position + centeroffset;
        }
        else
        {
            rb2d.constraints = RigidbodyConstraints2D.FreezeRotation;
        }

    }

    void OnMouseDrag()
    {
        if(enabled == false)
        {
            return;
        }

        if (isDragging)
        {
            cursorPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y, screenPoint.z);
            cursorPosition = Camera.main.ScreenToWorldPoint(cursorPoint) + offset;
            //transform.position = cursorPosition;
            CheckClosestPoint();
        }
    }

    void OnMouseUp()
    {
        if(enabled == false)
        {
            return;
        }


        isDragging = false;
        spriteRenderer.color = Color.white;
        SnapToClosestPoint();
    }

    void CheckClosestPoint()
    {

        float closestDistance = Mathf.Infinity;
        GameObject closestPoint = null;
        List<GameObject> availablePathPoints = pathPoints[currentTargetIndex].GetComponent<node>().connections;
        availablePathPoints.Add(pathPoints[currentTargetIndex]);

        foreach (GameObject point in availablePathPoints)
        {
            float distance = Vector3.Distance(cursorPosition, point.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPoint = point;
            }
        }

        if (closestPoint != null)
        {
            foreach (GameObject line in lines)
            {
                int CLPO = pathPoints.IndexOf(closestPoint);
                if (line.name == pathPoints[currentTargetIndex].name + "-" + pathPoints[CLPO].name || line.name == pathPoints[CLPO].name + "-" + pathPoints[currentTargetIndex].name)
                {
                    line.GetComponent<SpriteRenderer>().color = Color.green;
                }
                else
                {
                    print("line name = " + line.name + " maybe? = " + pathPoints[currentTargetIndex].name + "-" + pathPoints[CLPO].name);
                    line.GetComponent<SpriteRenderer>().color =  new Color(1.0f, 0.0f, 0.0f, 0.39f);;
                }
            }
        }
    }

    void SnapToClosestPoint()
    {
        float closestDistance = Mathf.Infinity;
        GameObject closestPoint = null;
        List<GameObject> availablePathPoints = pathPoints[currentTargetIndex].GetComponent<node>().connections;
        availablePathPoints.Add(pathPoints[currentTargetIndex]);

        foreach (GameObject point in availablePathPoints)
        {
            float distance = Vector3.Distance(cursorPosition, point.transform.position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPoint = point;
            }
        }

        if (closestPoint != null)
        {
            foreach (GameObject line in lines)
            {
                line.GetComponent<SpriteRenderer>().color = new Color(1.0f, 0.0f, 0.0f, 0.39f);
            }
            moving = true;
            targetposition = closestPoint.transform.position + centeroffset;
            StartCoroutine(MoveFromTo(transform.position, closestPoint.transform.position + centeroffset, 0.2f, false));
            previousTargetIndex = currentTargetIndex;
            currentTargetIndex = pathPoints.IndexOf(closestPoint);
        }
    }

    IEnumerator MoveFromTo(Vector3 start, Vector3 end, float time, bool dontmove)
    {
        print("Moving from " + start + " to " + end);
        float elapsedTime = 0;

        while (elapsedTime < time)
        {
            transform.position = Vector3.Lerp(start, end, elapsedTime / time);
            elapsedTime += Time.deltaTime;
            yield return null;
        }


        startposition = transform.position;
        moving = false;
        transform.position = end; // Ensure the object reaches the end position
    }

    private void TiggerOnEnter2D(Collider2D collision)
    {
        print("Collided with other object");
    }
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (moving){
            print("this is " + this.name + " start " + startposition);

            StopAllCoroutines();
            //print("position = " + transform.position + " previous = " + startposition);
            StartCoroutine(MoveFromTo(transform.position , startposition, 0.2f, true));
            currentTargetIndex = previousTargetIndex;
        }
    }
}
