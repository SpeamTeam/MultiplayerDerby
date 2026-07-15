using Assets.Scripts.Network;
using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Assets.Scripts.Menu
{
    public class MenuManager : MonoBehaviour
    {
        [SerializeField] NetworkHandler networkHandler;
        [SerializeField] TMP_InputField ipPortInputField;
        [SerializeField] TMP_InputField nickInputField;

        private void Start()
        {
            networkHandler = NetworkHandler.Instance;
        }

        public void OnHostPressed()
        {
            networkHandler.MakeHost(GetNickname());
        }
        public void OnConnectPressed()
        {
            string ip;
            ushort port;

            try
            {
                if (ipPortInputField.text.Equals(""))
                {
                    ip = "127.0.0.1";
                    port = 6767;
                }
                else
                {
                    string[] temp = ipPortInputField.text.Split(":");
                    ip = temp[0];
                    port = ushort.Parse(temp[1]);
                    Debug.Log($"IP: {ip}, PORT: {port}");
                }


                networkHandler.MakeClient(ip, port, GetNickname());
            }
            catch (IndexOutOfRangeException)
            {
                Debug.LogWarning("No semicolon in ip:port input field");
            }
            catch (FormatException e)
            {
                Debug.LogWarning("Format error, can't parse string: " + e.Message);
            }
        }

        public void OnExitGame()
        {
            Application.Quit();
        }

        private string GetNickname()
        {
            return nickInputField != null ? nickInputField.text : null;
        }
    }
}
