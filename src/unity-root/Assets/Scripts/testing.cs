using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Testing : MonoBehaviour
{
    [SerializeField] private int myNumber = 12;

    
    void Start()
    {
        myNumber = 13;
    }

    // Update is called once per frame
    void Update()
    {
        Debug.log("My number is: " + myNumber);
    }
}