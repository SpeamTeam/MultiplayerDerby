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
        public GameObject enemyPrefab;
        public GameObject cameraPrefab;
        public string worldSceneName = "WorldScene";
    }
}
