using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class DungeonFloorManager : MonoBehaviour, IDataPersistence
{
    [Header("Floor Settings")]
    public DungeonGenerator dungeonGeneratorPrefab;
    public float verticalFloorSpacing = 50f;
    public int maxFloors = 5;
    public float baseHeight = 1f;
    [Header("UI")]
    public GameObject loadingScreenUI;

    private Dictionary<int, GameObject> floors = new Dictionary<int, GameObject>();
    private Dictionary<int, Vector3> lastStairPositions = new Dictionary<int, Vector3>();
    private int currentFloorLevel = 1;
    private bool IsGenerationComplete { get; set; } = false;

    private static DungeonFloorManager instance;
    public static DungeonFloorManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindFirstObjectByType<DungeonFloorManager>();
            }
            return instance;
        }
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            // DontDestroyOnLoad(gameObject); // taken out so new level would load when scene reloaded
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if (dungeonGeneratorPrefab == null)
        {
            Debug.LogError("DungeonGenerator prefab not assigned to DungeonFloorManager!");
            return;
        }

        if (loadingScreenUI != null)
        {
            loadingScreenUI.SetActive(false);
        }

        if (LevelManager.IsLoadedFloor)
        {
            // load data
            Debug.Log("loading floor from file...");
        }
        else
        {
            Debug.Log("generating random floor");
            GenerateFirstFloor();
        }

    }

    // public void LoadData(GameData data)
    // {
    //     for (int i = 0; i < data.roomPositions.Count; i++)
    //     {
    //         int roomTypeIndex = data.roomTypes[i];
    //         GameObject roomPrefab = GetComponent<RoomListManager>().GetRoomPrefab(roomTypeIndex);
    //         Vector3 roomPos = data.roomPositions[i];

    //         GameObject room = Instantiate(roomPrefab, roomPos, Quaternion.identity);
    //         // don't think setting a parent is necessary
    //     }
    // }

    public void LoadData(GameData data)
    {
        StartCoroutine(LoadDungeonSequence(data));
    }

    private IEnumerator LoadDungeonSequence(GameData data)
    {
        Debug.Log($"Starting to load {data.roomPositions.Count} rooms");
        if (loadingScreenUI != null)
        {
            loadingScreenUI.SetActive(true);
        }

        try
        {
            // Clear existing floor
            foreach (var floor in floors.Values)
            {
                if (floor != null)
                {
                    Destroy(floor);
                }
            }
            floors.Clear();

            GameObject floorContainer = new GameObject($"Floor_1");
            floorContainer.transform.position = new Vector3(0, baseHeight, 0);
            floors[1] = floorContainer;

            RoomListManager roomList = GetComponent<RoomListManager>();
            if (roomList == null)
            {
                Debug.LogError("No RoomListManager found!");
                yield break;
            }

            // First validate all room types
            for (int i = 0; i < data.roomTypes.Count; i++)
            {
                int roomType = data.roomTypes[i];
                if (roomType < 0 || roomType >= roomList.roomPrefabs.Length)
                {
                    Debug.LogError($"Invalid room type {roomType} at index {i}. Valid range: 0-{roomList.roomPrefabs.Length - 1}");
                    yield break;
                }
            }

            List<RoomBehaviour> loadedRooms = new List<RoomBehaviour>();
            Dictionary<Vector3, RoomBehaviour> roomPositions = new Dictionary<Vector3, RoomBehaviour>();

            for (int i = 0; i < data.roomPositions.Count; i++)
            {
                int roomTypeIndex = data.roomTypes[i];
                GameObject roomPrefab = roomList.GetRoomPrefab(roomTypeIndex);
                Vector3 roomPos = data.roomPositions[i];

                Debug.Log($"Loading room type {roomTypeIndex} at position {roomPos}");
                GameObject room = Instantiate(roomPrefab, roomPos, Quaternion.identity, floorContainer.transform);

                RoomBehaviour roomBehaviour = room.GetComponent<RoomBehaviour>();
                if (roomBehaviour != null)
                {
                    loadedRooms.Add(roomBehaviour);
                    // Storing this for the door positions
                    roomPositions[roomPos] = roomBehaviour;
                    for (int j = 0; j < roomBehaviour.wallSections.Length; j++)
                    {
                        roomBehaviour.ShowWall(j);
                    }
                }

                yield return null;
            }

            // Give time for rooms to fully initialize
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.5f);

            // Reset all doors before processing connections
            Debug.Log("Resetting doors before connection...");
            foreach (var roomBehaviour in loadedRooms)
            {
                for (int i = 0; i < roomBehaviour.wallSections.Length; i++)
                {
                    if (roomBehaviour.wallSections[i].completeWall != null)
                    {
                        roomBehaviour.wallSections[i].completeWall.SetActive(true);
                    }
                    if (roomBehaviour.wallSections[i].doorwayOpen != null)
                    {
                        roomBehaviour.wallSections[i].doorwayOpen.SetActive(false);
                    }
                }
            }

            // Restore doorpoints
            if (data.doorStates != null && data.doorStates.Count > 0)
            {
                Debug.Log("Restoring door states...");
                foreach (var doorData in data.doorStates)
                {
                    if (roomPositions.TryGetValue(doorData.roomPosition, out RoomBehaviour room))
                    {
                        if (doorData.wallIndex < room.wallSections.Length)
                        {
                            if (doorData.isDoorway)
                            {
                                room.ShowDoorway(doorData.wallIndex);
                            }
                            else
                            {
                                room.ShowWall(doorData.wallIndex);
                            }
                        }
                    }
                }
            }

            // Process door connections
            Debug.Log("Processing door connections...");
            DungeonConnectionHandler.Instance.ProcessConnections();

            // Wait for door connections with timeout
            float timeoutDuration = 5f;
            float elapsedTime = 0f;
            while (DungeonConnectionHandler.Instance.IsProcessingConnections && elapsedTime < timeoutDuration)
            {
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            // More wait time after connections are processed
            yield return new WaitForSeconds(0.2f);

            // Setup player spawn
            yield return StartCoroutine(TeleportPlayerToNewFloor(floorContainer));

            currentFloorLevel = 1;
            IsGenerationComplete = true;

            Debug.Log("Dungeon loading complete!");
        }
        finally
        {
            if (loadingScreenUI != null)
            {
                loadingScreenUI.SetActive(false);
            }
        }
    }

    // public void SaveData(ref GameData data)
    // {
    //     if(data.roomPositions == null || data.roomTypes == null) {
    //         data.roomPositions = new List<Vector3>();
    //         data.roomTypes = new List<int>();
    //     }
    //     else {
    //         // clear existing data before saving
    //         Debug.Log("clearing room data");
    //         data.roomPositions.Clear();
    //         data.roomTypes.Clear();
    //     }

    //     GameObject[] rooms = GameObject.FindGameObjectsWithTag("Room");
    //     foreach(GameObject room in rooms) {
    //         data.roomPositions.Add(room.transform.position);
    //         int roomType = room.GetComponent<RoomID>().GetRoomID();
    //         data.roomTypes.Add(roomType);
    //     }
    // }

    public void SaveData(ref GameData data)
    {
        // Clear existing room data
        Debug.Log("clearing room data");
        data.roomPositions.Clear();
        data.roomTypes.Clear();
        data.doorStates.Clear();

        // Find all rooms by RoomID component
        RoomID[] allRooms = FindObjectsOfType<RoomID>(true);
        Debug.Log($"Found {allRooms.Length} rooms to save");

        foreach (RoomID room in allRooms)
        {
            if (room == null || room.gameObject == null) continue;

            // Save room position and type
            data.roomPositions.Add(room.transform.position);
            int roomType = room.GetRoomID();
            Debug.Log($"Saving room {room.gameObject.name} at position {room.transform.position} with type {roomType}");
            data.roomTypes.Add(roomType);

            // Save door states for this room
            RoomBehaviour roomBehaviour = room.GetComponent<RoomBehaviour>();
            if (roomBehaviour != null)
            {
                for (int i = 0; i < roomBehaviour.wallSections.Length; i++)
                {
                    var wallSection = roomBehaviour.wallSections[i];
                    bool isDoorway = wallSection.doorwayOpen != null &&
                                   wallSection.doorwayOpen.activeSelf;

                    data.doorStates.Add(new GameData.DoorData
                    {
                        roomPosition = room.transform.position,
                        wallIndex = i,
                        isDoorway = isDoorway
                    });
                }
            }
        }
        // Probably dont need this. This is just Debug to see how many rooms we are saving
        Debug.Log($"Saved total of {data.roomPositions.Count} rooms with positions and types");
    }

    private void GenerateFirstFloor()
    {
        GenerateNewFloor(1);
    }

    public void GenerateNewFloor(int floorLevel)
    {
        if (floorLevel > maxFloors)
        {
            Debug.LogWarning($"Attempted to generate floor {floorLevel} which exceeds max floors of {maxFloors}");
            return;
        }

        StartCoroutine(GenerateFloorSequence(floorLevel));
    }

    private IEnumerator GenerateFloorSequence(int floorLevel)
    {
        try
        {
            if (dungeonGeneratorPrefab == null)
            {
                Debug.LogError("Cannot generate floor: DungeonGenerator prefab is null!");
                yield break;
            }

            Debug.Log($"Starting generation of floor {floorLevel}");

            if (loadingScreenUI != null)
            {
                loadingScreenUI.SetActive(true);
            }

            // Create new floor container
            GameObject newFloorContainer = new GameObject($"Floor_{floorLevel}");
            float floorHeight = (verticalFloorSpacing * (floorLevel - 1)) + baseHeight;
            newFloorContainer.transform.position = new Vector3(0, floorHeight, 0);

            // Setup new DungeonGenerator
            DungeonGenerator generator = newFloorContainer.AddComponent<DungeonGenerator>();
            if (generator == null)
            {
                Debug.LogError("Failed to add DungeonGenerator component!");
                Destroy(newFloorContainer);
                yield break;
            }

            CopyGeneratorSettings(generator);

            // Handle floor management
            if (floors.ContainsKey(floorLevel))
            {
                Debug.Log($"Removing existing floor {floorLevel}");
                if (floors[floorLevel] != null)
                {
                    Destroy(floors[floorLevel]);
                    yield return null;
                }
            }

            floors[floorLevel] = newFloorContainer;

            // Wait for generation with timeout
            float timeoutDuration = 10f;
            float elapsedTime = 0f;
            while (!generator.IsGenerationComplete && elapsedTime < timeoutDuration)
            {
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            if (!generator.IsGenerationComplete)
            {
                Debug.LogError("Dungeon generation timed out!");
                Destroy(newFloorContainer);
                yield break;
            }

            // Hide previous floor
            if (floors.ContainsKey(currentFloorLevel) && currentFloorLevel != floorLevel)
            {
                if (floors[currentFloorLevel] != null)
                {
                    floors[currentFloorLevel].SetActive(false);
                    Debug.Log($"Deactivated floor {currentFloorLevel}");
                }
            }

            yield return StartCoroutine(TeleportPlayerToNewFloor(newFloorContainer));

            // Wait for physics to settle
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(0.2f);

            currentFloorLevel = floorLevel;
            Debug.Log($"Floor {floorLevel} generation complete");

            Debug.Log("Resetting all doors...");
            generator.ResetAllDoors();  // Use the generator's method

            DungeonConnectionHandler.Instance.ProcessConnections();

            // Wait for door connections with timeout
            elapsedTime = 0f;
            while (DungeonConnectionHandler.Instance.IsProcessingConnections && elapsedTime < timeoutDuration)
            {
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            if (DungeonConnectionHandler.Instance.IsProcessingConnections)
            {
                Debug.LogWarning("Door connection process timed out!");
            }

            IsGenerationComplete = true;
        }
        finally
        {
            if (loadingScreenUI != null)
            {
                loadingScreenUI.SetActive(false);
            }
        }
    }

    private IEnumerator TeleportPlayerToNewFloor(GameObject newFloor)
    {
        yield return new WaitForEndOfFrame();

        // Find player
        GameObject player = null;

        player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            player = GameObject.Find("Player");
        }

        // As a last resort, try finding the player controller component directly
        if (player == null)
        {
            CharacterController[] controllers = FindObjectsOfType<CharacterController>();
            foreach (var controller in controllers)
            {
                // You might want to add additional checks here specific to your player object
                if (controller.gameObject.name.ToLower().Contains("player"))
                {
                    player = controller.gameObject;
                    break;
                }
            }
        }

        if (player == null)
        {
            Debug.LogError("Player not found using any method! Please ensure the player object exists in the scene.");
            yield break;
        }

        PlayerSpawnPoint spawnPointComponent = newFloor.GetComponentInChildren<PlayerSpawnPoint>(true);
        if (spawnPointComponent != null)
        {
            Rigidbody rb = player.GetComponent<Rigidbody>();
            CharacterController cc = player.GetComponent<CharacterController>();

            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            }

            if (cc != null)
            {
                cc.enabled = false;
            }

            player.transform.position = spawnPointComponent.transform.position;

            yield return new WaitForFixedUpdate();

            if (cc != null)
            {
                cc.enabled = true;
            }

            if (rb != null)
            {
                rb.isKinematic = false;
            }
        }
        else
        {
            Debug.LogError("No spawn point found in new floor!");
        }

        // save data when player moves to new floor
        LevelManager levelManager = FindAnyObjectByType<LevelManager>();
        levelManager.SaveCheckpoint();
    }

    private void CopyGeneratorSettings(DungeonGenerator generator)
    {
        if (dungeonGeneratorPrefab == null || generator == null) return;

        generator.startingRoomPrefab = dungeonGeneratorPrefab.startingRoomPrefab;
        generator.stairRoomPrefab = dungeonGeneratorPrefab.stairRoomPrefab;
        generator.normalRoomPrefabs = dungeonGeneratorPrefab.normalRoomPrefabs;
        generator.largeRoomPrefabs = dungeonGeneratorPrefab.largeRoomPrefabs;
        generator.longRoomPrefabs = dungeonGeneratorPrefab.longRoomPrefabs;

        generator.useRandomCounts = dungeonGeneratorPrefab.useRandomCounts;
        generator.numberOfNormalRooms = dungeonGeneratorPrefab.numberOfNormalRooms;
        generator.numberOfLargeRooms = dungeonGeneratorPrefab.numberOfLargeRooms;
        generator.numberOfLongRooms = dungeonGeneratorPrefab.numberOfLongRooms;

        generator.normalRoomRange = dungeonGeneratorPrefab.normalRoomRange;
        generator.largeRoomRange = dungeonGeneratorPrefab.largeRoomRange;
        generator.longRoomRange = dungeonGeneratorPrefab.longRoomRange;
        generator.roomSpacing = dungeonGeneratorPrefab.roomSpacing;
        generator.minimumDistanceFromStart = dungeonGeneratorPrefab.minimumDistanceFromStart;

        /*
        generator.SetupEnemySpawner();

        EnemySpawner originalSpawner = dungeonGeneratorPrefab.GetComponent<EnemySpawner>();
        if (originalSpawner != null)
        {
            EnemySpawner newSpawner = generator.GetComponent<EnemySpawner>();
            newSpawner.enemyPrefabs = originalSpawner.enemyPrefabs;
            newSpawner.baseSpawnChance = originalSpawner.baseSpawnChance;
            newSpawner.incrementPerFloor = originalSpawner.incrementPerFloor;
            newSpawner.maxSpawnChance = originalSpawner.maxSpawnChance;
            Debug.Log($"Enemy spawner set up with {newSpawner.enemyPrefabs.Count} prefabs and base chance {newSpawner.baseSpawnChance}");
        }
        else
        {
            Debug.LogWarning("Original spawner not found on prefab!");
        }
        */
    }

    public void MoveToNextFloor(Vector3 stairPosition)
    {
        /*
        int nextFloorLevel = currentFloorLevel + 1;
        if (nextFloorLevel <= maxFloors)
        {
            lastStairPositions[currentFloorLevel] = stairPosition;
            Debug.Log($"Stored stair position for floor {currentFloorLevel}: {stairPosition}");
            GenerateNewFloor(nextFloorLevel);
        }
        else
        {
            Debug.Log("Reached maximum floor level!");
        }
        */

        // changes done by Matthew for the sake of the project
        LevelManager levelManager = FindAnyObjectByType<LevelManager>();
        levelManager.GoDownFloor();
    }

    public void ReturnToPreviousFloor()
    {
        int previousFloor = currentFloorLevel - 1;

        if (previousFloor < 1)
        {
            Debug.Log("Already on first floor!");
            return;
        }

        if (floors.ContainsKey(previousFloor))
        {
            if (floors.ContainsKey(currentFloorLevel))
            {
                floors[currentFloorLevel].SetActive(false);
            }

            floors[previousFloor].SetActive(true);
            StartCoroutine(ReprocessPreviousFloor(previousFloor));
        }
    }

    private IEnumerator ReprocessPreviousFloor(int floorLevel)
    {
        if (!floors.ContainsKey(floorLevel)) yield break;

        GameObject floor = floors[floorLevel];
        if (floor == null)
        {
            Debug.LogError($"Floor {floorLevel} is null!");
            yield break;
        }

        DungeonGenerator generator = floor.GetComponent<DungeonGenerator>();
        if (generator != null)
        {
            generator.ResetAllDoors();
        }

        yield return new WaitForFixedUpdate();
        yield return new WaitForSeconds(0.2f);

        DungeonConnectionHandler.Instance.ProcessConnections();

        float timeoutDuration = 5f;
        float elapsedTime = 0f;
        while (DungeonConnectionHandler.Instance.IsProcessingConnections && elapsedTime < timeoutDuration)
        {
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (lastStairPositions.ContainsKey(floorLevel))
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;

                player.transform.position = lastStairPositions[floorLevel];
                yield return new WaitForFixedUpdate();

                if (cc != null) cc.enabled = true;
            }
        }

        currentFloorLevel = floorLevel;
    }
}