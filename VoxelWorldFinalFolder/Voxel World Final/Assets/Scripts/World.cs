using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public struct PerlinSettings
{
    public float heightScale;
    public float scale;
    public int octaves;
    public float heightOffset;
    public float probability;

    public PerlinSettings(float hs, float s, int o, float ho, float p)
    {
        heightScale = hs;
        scale = s;
        octaves = o;
        heightOffset = ho;
        probability = p;
    }
}


public class World : MonoBehaviour
{
    public static Vector3Int worldDimensions = new Vector3Int(20, 20, 20);
    public static Vector3Int extraWorldDimensions = new Vector3Int(5, 5, 5);
    public static Vector3Int chunkDimensions = new Vector3Int(10, 10, 10);
    public bool loadFromFile = false;
    public GameObject chunkPrefab;
    public GameObject mCamera;
    public GameObject fpc;
    public Slider loadingBar;

    public static PerlinSettings surfaceSettings;
    public PerlinGrapher surface;

    public static PerlinSettings stoneSettings;
    public PerlinGrapher stone;

    public static PerlinSettings diamondTSettings;
    public PerlinGrapher diamondT;

    public static PerlinSettings diamondBSettings;
    public PerlinGrapher diamondB;

    public static PerlinSettings caveSettings;
    public Perlin3DGrapher caves;

    public static PerlinSettings treeSettings;
    public Perlin3DGrapher tree;

    public static PerlinSettings biomeSettings;
    public Perlin3DGrapher biome;

    public HashSet<Vector3Int> chunkChecker = new HashSet<Vector3Int>();
    public HashSet<Vector2Int> chunkColumns = new HashSet<Vector2Int>();
    public Dictionary<Vector3Int, Chunk> chunks = new Dictionary<Vector3Int, Chunk>();

    Vector3Int lastBuildPosition;
    int drawRadius = 5;

    Queue<IEnumerator> buildQueue = new Queue<IEnumerator>();

    IEnumerator BuildCoordinator()
    {
        while (true)
        {
            while (buildQueue.Count > 0)
                yield return StartCoroutine(buildQueue.Dequeue());
            yield return null;
        }
    }

    public void SaveWorld()
    {
        FileSaver.Save(this);
    }

    IEnumerator LoadWorldFromFile()
    {
        WorldData wd = FileSaver.Load();
        if (wd == null)
        {
            StartCoroutine(BuildWorld());
            yield break;
        }

        chunkChecker.Clear();
        for (int i = 0; i < wd.chunkCheckerValues.Length; i += 3)
        {
            chunkChecker.Add(new Vector3Int(wd.chunkCheckerValues[i],
                wd.chunkCheckerValues[i + 1],
                wd.chunkCheckerValues[i + 2]));
        }

        chunkColumns.Clear();
        for (int i = 0; i < wd.chunkColumnValues.Length; i += 2)
        {
            chunkColumns.Add(new Vector2Int(wd.chunkColumnValues[i],
                                                wd.chunkColumnValues[i + 1]));
        }

        int index = 0;
        int vIndex = 0;
        loadingBar.maxValue = chunkChecker.Count;
        foreach (Vector3Int chunkPos in chunkChecker)
        {
            GameObject chunk = Instantiate(chunkPrefab);
            chunk.name = "Chunk_" + chunkPos.x + "_" + chunkPos.y + "_" + chunkPos.z;
            Chunk c = chunk.GetComponent<Chunk>();
            int blockCount = chunkDimensions.x * chunkDimensions.y * chunkDimensions.z;
            c.chunkData = new MeshUtils.BlockType[blockCount];
            c.healthData = new MeshUtils.BlockType[blockCount];

            for (int i = 0; i < blockCount; i++)
            {
                c.chunkData[i] = (MeshUtils.BlockType)wd.allChunkData[index];
                c.healthData[i] = MeshUtils.BlockType.NOCRACK;
                index++;
            }
            loadingBar.value++;
            c.CreateChunk(chunkDimensions, chunkPos, false);
            chunks.Add(chunkPos, c);
            RedrawChunk(c);
            c.meshRendererSolid.enabled = wd.chunkVisibility[vIndex];
            c.meshRendererFluid.enabled = wd.chunkVisibility[vIndex];
            vIndex++;
            yield return null;
        }

        fpc.transform.position = new Vector3(wd.fpcX, wd.fpcY, wd.fpcZ);
        mCamera.SetActive(false);
        fpc.SetActive(true);
        loadingBar.gameObject.SetActive(false);
        lastBuildPosition = Vector3Int.CeilToInt(fpc.transform.position);
        StartCoroutine(BuildCoordinator());
        StartCoroutine(UpdateWorld());

    }

    // Start is called before the first frame update
    void Start()
    {
        loadingBar.maxValue = worldDimensions.x * worldDimensions.z;

        surfaceSettings = new PerlinSettings(surface.heightScale, surface.scale,
                                     surface.octaves, surface.heightOffset, surface.probability);

        stoneSettings = new PerlinSettings(stone.heightScale, stone.scale,
                             stone.octaves, stone.heightOffset, stone.probability);

        diamondTSettings = new PerlinSettings(diamondT.heightScale, diamondT.scale,
                     diamondT.octaves, diamondT.heightOffset, diamondT.probability);

        diamondBSettings = new PerlinSettings(diamondB.heightScale, diamondB.scale,
                     diamondB.octaves, diamondB.heightOffset, diamondB.probability);

        caveSettings = new PerlinSettings(caves.heightScale, caves.scale,
             caves.octaves, caves.heightOffset, caves.DrawCutOff);

        treeSettings = new PerlinSettings(tree.heightScale, tree.scale,
            tree.octaves, tree.heightOffset, tree.DrawCutOff);

        biomeSettings = new PerlinSettings(biome.heightScale, biome.scale,
                biome.octaves, biome.heightOffset, biome.DrawCutOff);

        if (loadFromFile)
            StartCoroutine(LoadWorldFromFile());
        else
            StartCoroutine(BuildWorld());
    }

    MeshUtils.BlockType buildType = MeshUtils.BlockType.DIRT;
    public void SetBuildType(int type)
    {
        buildType = (MeshUtils.BlockType) type;
    }

    public static Vector3Int FromFlat(int i)
    {
        return new Vector3Int(i % chunkDimensions.x,
                                (i / chunkDimensions.x) % chunkDimensions.y,
                                i / (chunkDimensions.x * chunkDimensions.y));
    }

    public static int ToFlat(Vector3Int v)
    {
        return v.x + chunkDimensions.x * (v.y + chunkDimensions.z * v.z);
    }

    public System.Tuple<Vector3Int, Vector3Int> GetWorldNeighbour(Vector3Int blockIndex, Vector3Int chunkIndex)
    {
        Chunk thisChunk = chunks[chunkIndex];
        int bx = blockIndex.x;
        int by = blockIndex.y;
        int bz = blockIndex.z;

        Vector3Int neighbour = chunkIndex;
        if (bx == chunkDimensions.x)
        {
            neighbour = new Vector3Int((int)thisChunk.location.x + chunkDimensions.x,
                                        (int)thisChunk.location.y,
                                         (int)thisChunk.location.z);
            bx = 0;
        }
        else if (bx == -1)
        {
            neighbour = new Vector3Int((int)thisChunk.location.x - chunkDimensions.x,
                                        (int)thisChunk.location.y,
                                         (int)thisChunk.location.z);
            bx = chunkDimensions.x - 1;
        }
        else if (by == chunkDimensions.y)
        {
            neighbour = new Vector3Int((int)thisChunk.location.x,
                                        (int)thisChunk.location.y + chunkDimensions.y,
                                         (int)thisChunk.location.z);
            by = 0;
        }
        else if (by == -1)
        {
            neighbour = new Vector3Int((int)thisChunk.location.x,
                                        (int)thisChunk.location.y - chunkDimensions.y,
                                         (int)thisChunk.location.z);
            by = chunkDimensions.y - 1;
        }
        else if (bz == chunkDimensions.z)
        {
            neighbour = new Vector3Int((int)thisChunk.location.x,
                                        (int)thisChunk.location.y,
                                         (int)thisChunk.location.z + chunkDimensions.z);
            bz = 0;
        }
        else if (bz == -1)
        {
            neighbour = new Vector3Int((int)thisChunk.location.x,
                                        (int)thisChunk.location.y,
                                         (int)thisChunk.location.z - chunkDimensions.z);
            bz = chunkDimensions.z - 1;
        }

        return new System.Tuple<Vector3Int, 
        Vector3Int>(new Vector3Int(bx, by, bz), neighbour);


  
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            RaycastHit hit;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out hit, 10))
            {
                Vector3 hitBlock = Vector3.zero;
                if (Input.GetMouseButtonDown(0))
                {
                    hitBlock = hit.point - hit.normal / 2.0f;
                }
                else
                    hitBlock = hit.point + hit.normal / 2.0f;

                //Debug.Log("Block Location: " + hitBlock.x + "," + hitBlock.y + "," + hitBlock.z);
                Chunk thisChunk = hit.collider.gameObject.transform.parent.GetComponent<Chunk>();

                int bx = (int)(Mathf.Round(hitBlock.x) - thisChunk.location.x);
                int by = (int)(Mathf.Round(hitBlock.y) - thisChunk.location.y);
                int bz = (int)(Mathf.Round(hitBlock.z) - thisChunk.location.z);

                var blockNeighbour = GetWorldNeighbour(new Vector3Int(bx, by, bz), Vector3Int.CeilToInt(thisChunk.location));
                thisChunk = chunks[blockNeighbour.Item2];
                int i = ToFlat(blockNeighbour.Item1);

                if (Input.GetMouseButtonDown(0))
                {
                    if (MeshUtils.blockTypeHealth[(int)thisChunk.chunkData[i]] != -1)
                    {
                        if (thisChunk.healthData[i] == MeshUtils.BlockType.NOCRACK)
                            StartCoroutine(HealBlock(thisChunk, i));
                        thisChunk.healthData[i]++;
                        if (thisChunk.healthData[i] == MeshUtils.BlockType.NOCRACK +
                                                       MeshUtils.blockTypeHealth[(int)thisChunk.chunkData[i]])
                        {
                            thisChunk.chunkData[i] = MeshUtils.BlockType.AIR;
                            Vector3Int nBlock = FromFlat(i);
                            var neighbourBlock = GetWorldNeighbour(new Vector3Int(nBlock.x, nBlock.y + 1, nBlock.z),
                                                                    Vector3Int.CeilToInt(thisChunk.location));
                            Vector3Int block = neighbourBlock.Item1;
                            int neighbourBlockIndex = ToFlat(block);
                            Chunk neighbourChunk = chunks[neighbourBlock.Item2];
                            StartCoroutine(Drop(neighbourChunk, neighbourBlockIndex));
                        }
                    }
                }
                else
                {
                    thisChunk.chunkData[i] = buildType;
                    thisChunk.healthData[i] = MeshUtils.BlockType.NOCRACK;
                    StartCoroutine(Drop(thisChunk, i));
                }

                RedrawChunk(thisChunk);
            }
        }
    }

    void RedrawChunk(Chunk c)
    {
        c.CreateChunk(chunkDimensions, c.location, false);
    }

    WaitForSeconds threeSeconds = new WaitForSeconds(3);
    public IEnumerator HealBlock(Chunk c, int blockIndex)
    {
        yield return threeSeconds;
        if (c.chunkData[blockIndex] != MeshUtils.BlockType.AIR)
        {
            c.healthData[blockIndex] = MeshUtils.BlockType.NOCRACK;
            RedrawChunk(c);
        }
    }

    WaitForSeconds dropDelay = new WaitForSeconds(0.1f);
    public IEnumerator Drop(Chunk c, int blockIndex, int strength = 3)
    {
        if (!MeshUtils.canDrop.Contains(c.chunkData[blockIndex]))
            yield break;
        yield return dropDelay;
        while (true)
        {
            Vector3Int thisBlock = FromFlat(blockIndex);

            var neighbourBlock = GetWorldNeighbour(new Vector3Int(thisBlock.x, thisBlock.y - 1, thisBlock.z),
                                                    Vector3Int.CeilToInt(c.location));
            Vector3Int block = neighbourBlock.Item1;
            int neighbourBlockIndex = ToFlat(block);
            Chunk neighbourChunk = chunks[neighbourBlock.Item2];
            if (neighbourChunk != null && neighbourChunk.chunkData[neighbourBlockIndex] == MeshUtils.BlockType.AIR)
            {
                neighbourChunk.chunkData[neighbourBlockIndex] = c.chunkData[blockIndex];
                neighbourChunk.healthData[neighbourBlockIndex] = MeshUtils.BlockType.NOCRACK;

                var nBlockAbove = GetWorldNeighbour(new Vector3Int(thisBlock.x, thisBlock.y + 1, thisBlock.z),
                                                        Vector3Int.CeilToInt(c.location));
                Vector3Int blockAbove = nBlockAbove.Item1;
                int nBlockAboveIndex = ToFlat(blockAbove);
                Chunk nChunkAbove = chunks[nBlockAbove.Item2];


                c.chunkData[blockIndex] = MeshUtils.BlockType.AIR;
                StartCoroutine(Drop(nChunkAbove, nBlockAboveIndex));


                yield return dropDelay;
                RedrawChunk(c);
                if (neighbourChunk != c)
                    RedrawChunk(neighbourChunk);
                c = neighbourChunk;
                blockIndex = neighbourBlockIndex;

            }
            else if (MeshUtils.canFlow.Contains(c.chunkData[blockIndex]))
            {
                FlowIntoNeighbour(thisBlock, Vector3Int.CeilToInt(c.location), new Vector3Int(1, 0, 0), strength - 1);
                FlowIntoNeighbour(thisBlock, Vector3Int.CeilToInt(c.location), new Vector3Int(-1, 0, 0), strength - 1);
                FlowIntoNeighbour(thisBlock, Vector3Int.CeilToInt(c.location), new Vector3Int(0, 0, 1), strength - 1);
                FlowIntoNeighbour(thisBlock, Vector3Int.CeilToInt(c.location), new Vector3Int(0, 0, -1), strength - 1);
                yield break;
            }
            else
                yield break;
        }

    }

    public void FlowIntoNeighbour(Vector3Int blockPosition, Vector3Int chunkPosition, Vector3Int neighbourDirection, int strength)
    {
        strength--;
        if (strength <= 0) return;
        Vector3Int neighbourPosition = blockPosition + neighbourDirection;
        var neighbourBlock = GetWorldNeighbour(neighbourPosition, chunkPosition);
        Vector3Int block = neighbourBlock.Item1;
        int neighbourBlockIndex = ToFlat(block);
        Chunk neighbourChunk = chunks[neighbourBlock.Item2];
        if (neighbourChunk == null) return;
        if (neighbourChunk.chunkData[neighbourBlockIndex] == MeshUtils.BlockType.AIR)
        {
            neighbourChunk.chunkData[neighbourBlockIndex] = chunks[chunkPosition].chunkData[ToFlat(blockPosition)];
            RedrawChunk(neighbourChunk);
            StartCoroutine(Drop(neighbourChunk, neighbourBlockIndex, strength--));
        }

    }


    void BuildChunkColumn(int x, int z, bool meshEnabled = true)
    {
        for (int y = 0; y < worldDimensions.y; y++)
        {
            Vector3Int position = new Vector3Int(x, y * chunkDimensions.y, z);
            if (!chunkChecker.Contains(position))
            {
                GameObject chunk = Instantiate(chunkPrefab);
                chunk.name = "Chunk_" + position.x + "_" + position.y + "_" + position.z;
                Chunk c = chunk.GetComponent<Chunk>();
                c.CreateChunk(chunkDimensions, position);
                chunkChecker.Add(position);
                chunks.Add(position, c);
            }
            chunks[position].meshRendererSolid.enabled = meshEnabled;
            chunks[position].meshRendererFluid.enabled = meshEnabled;
        }
        chunkColumns.Add(new Vector2Int(x, z));
    }

    IEnumerator BuildExtraWorld()
    {
        int zEnd = worldDimensions.z + extraWorldDimensions.z;
        int zStart = worldDimensions.z - 1;
        int xEnd = worldDimensions.x + extraWorldDimensions.x;
        int xStart = worldDimensions.x - 1;


        for (int z = zStart; z < zEnd; z++)
        {
            for (int x = 0; x < xEnd; x++)
            {
                BuildChunkColumn(x * chunkDimensions.x, z * chunkDimensions.z, false);
                yield return null;
            }

        }

        for (int z = 0; z < zEnd; z++)
        {
            for (int x = xStart; x < xEnd; x++)
            {
                BuildChunkColumn(x * chunkDimensions.x, z * chunkDimensions.z, false);
                yield return null;
            }

        }


    }


    IEnumerator BuildWorld()
    {
        for (int z = 0; z < worldDimensions.z; z++)
        {
            for (int x = 0; x < worldDimensions.x; x++)
            {
                BuildChunkColumn(x * chunkDimensions.x, z * chunkDimensions.z);
                loadingBar.value++;
                yield return null;
            }
            
        }

        mCamera.SetActive(false);

        int xpos = (worldDimensions.x * chunkDimensions.x) / 2;
        int zpos = (worldDimensions.z * chunkDimensions.z) / 2;

        int ypos = (int)MeshUtils.fBM(xpos, zpos, surfaceSettings.octaves, surfaceSettings.scale, surfaceSettings.heightScale, surfaceSettings.heightOffset) + 10;
        fpc.transform.position = new Vector3Int(xpos, ypos, zpos);
        loadingBar.gameObject.SetActive(false);
        fpc.SetActive(true);
        lastBuildPosition = Vector3Int.CeilToInt(fpc.transform.position);
        StartCoroutine(BuildCoordinator());
        //StartCoroutine(UpdateWorld());
        StartCoroutine(BuildExtraWorld());
    }



    WaitForSeconds wfs = new WaitForSeconds(0.5f);
    IEnumerator UpdateWorld()
    {
        while (true)
        {
            if ((lastBuildPosition - fpc.transform.position).magnitude > (chunkDimensions.x))
            {
                lastBuildPosition = Vector3Int.CeilToInt(fpc.transform.position);
                int posx = (int)(fpc.transform.position.x / chunkDimensions.x) * chunkDimensions.x;
                int posz = (int)(fpc.transform.position.z / chunkDimensions.z) * chunkDimensions.z;
                buildQueue.Enqueue(BuildRecursiveWorld(posx, posz, drawRadius));
                buildQueue.Enqueue(HideColumns(posx, posz));
            }
            yield return wfs;
        }
    }

    public void HideChunkColumn(int x, int z)
    {
        for (int y = 0; y < worldDimensions.y; y++)
        {
            Vector3Int pos = new Vector3Int(x, y * chunkDimensions.y, z);
            if (chunkChecker.Contains(pos))
            {
                chunks[pos].meshRendererSolid.enabled = false;
                chunks[pos].meshRendererFluid.enabled = false;
            }
        }
    }

    IEnumerator HideColumns(int x, int z)
    {
        Vector2Int fpcPos = new Vector2Int(x, z);
        foreach (Vector2Int cc in chunkColumns)
        {
            if ((cc - fpcPos).magnitude >= drawRadius * chunkDimensions.x)
            {
                HideChunkColumn(cc.x, cc.y);
            }
        }
        yield return null;
    }

    IEnumerator BuildRecursiveWorld(int x, int z, int rad)
    {
        int nextrad = rad - 1;
        if (rad <= 0) yield break;

        BuildChunkColumn(x, z + chunkDimensions.z);
        buildQueue.Enqueue(BuildRecursiveWorld(x, z + chunkDimensions.z, nextrad));
        yield return null;

        BuildChunkColumn(x, z - chunkDimensions.z);
        buildQueue.Enqueue(BuildRecursiveWorld(x, z - chunkDimensions.z, nextrad));
        yield return null;

        BuildChunkColumn(x + chunkDimensions.x, z);
        buildQueue.Enqueue(BuildRecursiveWorld(x + chunkDimensions.x, z, nextrad));
        yield return null;

        BuildChunkColumn(x - chunkDimensions.x, z);
        buildQueue.Enqueue(BuildRecursiveWorld(x - chunkDimensions.x, z, nextrad));
        yield return null;
    }

}
