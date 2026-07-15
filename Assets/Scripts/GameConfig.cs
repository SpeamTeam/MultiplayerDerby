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

        [Header("Camera")]
        public GameObject cameraPrefab;
        public GameObject cameraTargetPrefab;

        [Header("Camera Settings")]
        public float distance = 4f;

        [Header("Scene names")]
        public string worldSceneName = "WorldScene";
        public string menuSceneName = "MenuScene";

        [Header("Respawn")]
        [Tooltip("Респавнить ли машину автоматически после смерти")]
        public bool autoRespawn = true;

        [Tooltip("Задержка (в секундах) между смертью и респавном. Используется ТОЛЬКО в fallback-ветке (кинематик выключен или нет префаба дрона). При включённом useCinematicRespawn тайминг задаётся раскадровкой ниже.")]
        public float respawnDelay = 3f;

        [Header("Cinematic Respawn (дрон + ящик)")]
        [Tooltip("Включить кинематографический респавн: дрон прилетает, сбрасывает ящик, ящик растворяется, на его месте появляется машина. Если выключено (или не назначен префаб дрона) — работает старый путь через respawnDelay.")]
        public bool useCinematicRespawn = true;

        [Tooltip("Префаб дрона респавна. Должен быть NetworkObject и зарегистрирован в сетевых префабах. Если не назначен — кинематик отключается и работает старый respawnDelay.")]
        public GameObject respawnDronePrefab;

        [Tooltip("Префаб ящика, который несёт дрон. Должен быть NetworkObject и зарегистрирован в сетевых префабах.")]
        public GameObject respawnCratePrefab;

        [Tooltip("Момент (сек от смерти), к которому дрон должен быть над целью и начать фазу сброса ящика. По раскадровке: 0–2 камера на машине, 2–7 обзорные камеры, с 7-й секунды вступает дрон.")]
        public float droneDeliveryStartTime = 7f;

        [Tooltip("Сколько секунд дрон летит от своей точки спавна к точке над целью. Сервер спавнит дрон в момент (droneDeliveryStartTime - droneTravelDuration), чтобы он прибыл ровно к началу фазы сброса.")]
        public float droneTravelDuration = 5f;

        [Tooltip("Высота (мировые единицы) точки зависания дрона над местом респавна машины, откуда сбрасывается ящик.")]
        public float droneOverheadHeight = 35f;

        [Tooltip("Сколько секунд ждать после сброса, пока ящик приземлится и уляжется, прежде чем запускать растворение.")]
        public float crateFallSettleTime = 1.5f;

        [Tooltip("Длительность (сек) плавного растворения ящика (fade материала + сжатие). Синхронно у всех клиентов.")]
        public float crateDissolveDuration = 1.5f;

        [Header("Menus")]
        public GameObject pauseMenuPrefab;
        public GameObject scoreMenuPrefab;

        [Header("Lobby")]
        [Tooltip("Сколько слотов открыто по умолчанию при создании лобби")]
        public int defaultMaxPlayers = 4;

        [Tooltip("Минимально допустимое значение слайдера MaxPlayers")]
        public int minPlayers = 1;

        [Tooltip("Абсолютная ёмкость списка слотов (верхняя граница слайдера MaxPlayers)")]
        public int lobbySlotCap = 8;
    }
}
