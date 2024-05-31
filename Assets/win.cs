using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class win : MonoBehaviour
{
    GameObject[] puzzelpieces;
    bool won = false; 
    // Start is called before the first frame update
    void Start()
    {
        puzzelpieces = GameObject.FindGameObjectsWithTag("puzzelpiece");
    }

    // Update is called once per frame
    void Update()
    {
        if (!won)
        {
            int amount = 0;
            foreach (GameObject piece in puzzelpieces)
            {
                if (piece.GetComponent<PuzzlePieceController>().winplace != piece.GetComponent<PuzzlePieceController>().currentTargetIndex)
                {
                    return;
                }
                else
                {
                    amount += 1;
                }
            }

            if (amount == puzzelpieces.Length)
            {
                Debug.Log("You win!");
                won = true;
            }
        }
    }
}
