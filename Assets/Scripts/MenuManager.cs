using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class MenuManager : MonoBehaviour
{
    [SerializeField] NetworkHandler networkHandler;
    [SerializeField] TMP_InputField ipPortInputField;

    public void OnHostPressed()
    {
        networkHandler.MakeHost();
    }
    public void OnConnectPressed()
    {
        string ip;
        int port;

        try {
            string[] temp = ipPortInputField.text.Split(":");
            ip = temp[0];
            port = int.Parse(temp[1]);
            Debug.Log($"IP: {ip}, PORT: {port}");
            

            networkHandler.MakeClient(ip, port);
        }
        catch (IndexOutOfRangeException) {
            Debug.LogWarning("No semicolon in ip:port input field");
        }
        catch (FormatException e)
        {
            Debug.LogWarning("Format error, can't parse string: " + e.Message);
        }
    }
}
