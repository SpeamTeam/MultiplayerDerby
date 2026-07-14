using System;
using System.Collections.Generic;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;

namespace Assets.Scripts
{
    [CreateAssetMenu(fileName = "gameconfig", menuName = "gameConfig")]
    public class GameConfig : ScriptableObject
    {
        public GameObject playerPrefab;
        public GameObject RagDollPrefab;
        public GameObject botPrefab;
        public GameObject cameraPrefab;

        public string worldSceneName = "WorldScene";
        public string menuSceneName = "MenuScene";

        [Header("Respawn")]
        [Tooltip("Респавнить ли машину автоматически после смерти")]
        public bool autoRespawn = true;

        [Tooltip("Задержка (в секундах) между смертью и респавном")]
        public float respawnDelay = 3f;

        [Header("Menus")]
        public GameObject pauseMenuPrefab;
        public GameObject scoreMenuPrefab;
    }
}
