using System;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace FoundationOneBrickNoSoil
{
    /// <summary>
    /// Incremental equatorial terraforming. Latitude rows are selected from
    /// PlatformSystem.reformOffsets, not guessed from a fixed planet radius.
    /// The north and south sides of the equator are stored in separate halves
    /// of the reform row array in DSP.
    ///
    /// This runs on the local planet. It does not modify buildings or veins.
    /// </summary>
    internal sealed class EquatorPainter
    {
        private const int TilesPerFrame = 96;
        private static readonly Bounds FoundationBounds =
            new Bounds(Vector3.zero, new Vector3(0.33f, 0.08000001f, 0.33f));

        private readonly ManualLogSource log;
        private readonly ConfigEntry<int> northSetting;
        private readonly ConfigEntry<int> southSetting;

        private bool showing;
        private bool confirmationPending;
        private Rect windowRect = new Rect(65f, 115f, 325f, 252f);
        private string northText;
        private string southText;
        private string status = "Ready";

        private PlanetData planet;
        private PlanetFactory factory;
        private PlatformSystem platform;
        private int halfLatitudeRows;
        private int northRows;
        private int southRows;
        private int phase;
        private int row;
        private int tileIndex;
        private int tileEnd;
        private int selectedBrushType;
        private int selectedBrushColor;
        private long tilesDone;
        private long totalTiles;
        private bool working;

        internal EquatorPainter(ConfigFile config, ManualLogSource logger)
        {
            log = logger;
            northSetting = config.Bind("Equator", "NorthTiles", 10,
                "Number of latitude rows to foundation north of the equator (0 = none).");
            southSetting = config.Bind("Equator", "SouthTiles", 10,
                "Number of latitude rows to foundation south of the equator (0 = none).");
            northText = northSetting.Value.ToString();
            southText = southSetting.Value.ToString();
        }

        private static bool IsAvailable()
        {
            return GameMain.isRunning && GameMain.localPlanet != null &&
                GameMain.localPlanet.type != EPlanetType.Gas &&
                GameMain.mainPlayer != null &&
                GameMain.mainPlayer.factory != null &&
                GameMain.mainPlayer.factory.platformSystem != null;
        }

        internal void DrawGui()
        {
            if (!IsAvailable()) return;

            if (!showing)
            {
                if (GUI.Button(new Rect(65f, 115f, 170f, 32f), "Equator Foundations"))
                    showing = true;
                return;
            }
            windowRect = GUI.Window(347681, windowRect, DrawWindow, "Equator Foundations");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(8);
            GUILayout.Label("Foundation rows on each side of the equator:");
            GUILayout.BeginHorizontal();
            GUILayout.Label("North", GUILayout.Width(65));
            northText = GUILayout.TextField(northText, 5, GUILayout.Width(70));
            GUILayout.Space(10);
            GUILayout.Label("South", GUILayout.Width(55));
            southText = GUILayout.TextField(southText, 5, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            if (working)
            {
                GUILayout.Label("Painting: " + tilesDone + " / " + totalTiles + " tiles");
                if (GUILayout.Button("Cancel")) Cancel("Cancelled; already-painted tiles remain");
            }
            else if (confirmationPending)
            {
                GUILayout.Label("This modifies the planet and is not undoable.");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Confirm Fill")) BeginFill();
                if (GUILayout.Button("Back")) confirmationPending = false;
                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("Fill Equator"))
                {
                    int north, south;
                    if (TryParseWidths(out north, out south))
                    {
                        northSetting.Value = north;
                        southSetting.Value = south;
                        confirmationPending = true;
                        status = "Confirm the selected band";
                    }
                }
            }

            GUILayout.Label(status);
            if (GUILayout.Button("Hide panel")) showing = false;
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 325f, 26f));
        }

        private bool TryParseWidths(out int north, out int south)
        {
            north = south = 0;
            if (!int.TryParse(northText, out north) ||
                !int.TryParse(southText, out south) ||
                north < 0 || south < 0 || (north == 0 && south == 0) ||
                north > 9999 || south > 9999)
            {
                status = "Enter nonnegative whole numbers (at least one > 0)";
                return false;
            }
            return true;
        }

        private void BeginFill()
        {
            confirmationPending = false;
            if (!IsAvailable())
            {
                status = "Land on a solid planet first";
                return;
            }
            try
            {
                planet = GameMain.localPlanet;
                factory = GameMain.mainPlayer.factory;
                platform = factory.platformSystem;
                platform.EnsureReformData();

                int latitudeCount = platform.latitudeCount;
                var offsets = platform.reformOffsets;
                if (latitudeCount <= 1 || latitudeCount % 2 != 0 ||
                    offsets == null || offsets.Length < latitudeCount)
                    throw new InvalidOperationException("Unexpected planet reform grid; refusing to paint.");

                halfLatitudeRows = latitudeCount / 2;
                northRows = Math.Min(northSetting.Value, halfLatitudeRows);
                southRows = Math.Min(southSetting.Value, halfLatitudeRows);
                if (northRows == 0 && southRows == 0)
                    throw new InvalidOperationException("Choose at least one row.");

                var actionBuild = GameMain.mainPlayer.controller.actionBuild;
                var reformTool = actionBuild.reformTool;
                selectedBrushType = reformTool.brushType;
                if (selectedBrushType != 1 && selectedBrushType != 2)
                    selectedBrushType = 1; // Always build, never select the removal brush.
                selectedBrushColor = reformTool.brushColor;

                totalTiles = 0;
                for (int r = 0; r < northRows; r++) totalTiles += RowSize(r);
                for (int r = 0; r < southRows; r++) totalTiles += RowSize(halfLatitudeRows + r);
                if (totalTiles == 0)
                    throw new InvalidOperationException("No Foundation tiles found for selected rows.");

                tilesDone = 0;
                phase = northRows > 0 ? 0 : 1;
                row = 0;
                PrepareRow();
                working = true;
                status = "Painting equator on planet " + planet.id;
                log.LogInfo("Equator fill started on planet " + planet.id +
                    ": north=" + northRows + ", south=" + southRows +
                    ", tiles=" + totalTiles);
            }
            catch (Exception ex)
            {
                working = false;
                status = "Failed to start; see BepInEx log";
                log.LogError("Equator fill initialization failed: " + ex);
            }
        }

        private int RowSize(int reformRow)
        {
            int start = platform.reformOffsets[reformRow];
            int end = reformRow + 1 < platform.reformOffsets.Length
                ? platform.reformOffsets[reformRow + 1]
                : platform.maxReformCount;
            if (end < start || end > platform.maxReformCount || start < 0)
                throw new InvalidOperationException("Invalid reform offset for row " + reformRow);
            return end - start;
        }

        private int CurrentReformRow()
        {
            return phase == 0 ? row : halfLatitudeRows + row;
        }

        private void PrepareRow()
        {
            int reformRow = CurrentReformRow();
            tileIndex = platform.reformOffsets[reformRow];
            tileEnd = tileIndex + RowSize(reformRow);
        }

        internal void Update()
        {
            if (!working) return;
            if (!IsAvailable() || !ReferenceEquals(planet, GameMain.localPlanet) ||
                !ReferenceEquals(factory, GameMain.mainPlayer.factory))
            {
                Cancel("Stopped: planet changed");
                return;
            }
            try
            {
                int paintedThisFrame = 0;
                // Same Begin/Flatten/End sequence as the stock Foundation tool.
                factory.BeginFlattenTerrain();
                while (paintedThisFrame < TilesPerFrame && working)
                {
                    if (tileIndex >= tileEnd)
                    {
                        if (!NextRow()) break;
                        continue;
                    }

                    int reformRow = CurrentReformRow();
                    int start = platform.reformOffsets[reformRow];
                    int tileCount = tileEnd - start;
                    int column = tileIndex - start;
                    float latStep = 180f / platform.latitudeCount;
                    float latitude = (row + 0.5f) * latStep * (phase == 0 ? 1 : -1);
                    float longitude = (column + 0.5f) * 360f / tileCount;
                    if (longitude > 180f) longitude -= 360f;
                    Vector3 center = ToPlanetPosition(latitude, longitude, planet.realRadius);

                    if (!platform.IsTerrainReformed(platform.GetReformType(tileIndex)))
                    {
                        factory.FlattenTerrain(center, Quaternion.identity, FoundationBounds,
                            3f, 1f, false, true, true, false, default(SkillTarget));
                    }
                    platform.SetReformType(tileIndex, selectedBrushType);
                    platform.SetReformColor(tileIndex, selectedBrushColor);

                    tileIndex++;
                    paintedThisFrame++;
                    tilesDone++;
                }
                factory.EndFlattenTerrain();

                if (planet.UpdateDirtyMeshes())
                    factory.RenderLocalPlanetHeightmap();

                if (!working)
                {
                    status = "Completed " + tilesDone + " Foundation tiles";
                    log.LogInfo("Equator fill completed (" + tilesDone + " tiles).");
                }
            }
            catch (Exception ex)
            {
                Cancel("Stopped after error; check BepInEx log");
                log.LogError("Equator fill stopped with an error: " + ex);
                // If the game's batch EndFlattenTerrain failed, do not try to
                // force it again: a second failure could damage game state.
            }
        }

        private bool NextRow()
        {
            row++;
            if (phase == 0 && row >= northRows)
            {
                phase = 1;
                row = 0;
            }
            if (phase == 1 && row >= southRows)
            {
                working = false;
                return false;
            }
            PrepareRow();
            return true;
        }

        private static Vector3 ToPlanetPosition(float latitude, float longitude, float radius)
        {
            float lat = latitude * Mathf.Deg2Rad;
            float lon = longitude * Mathf.Deg2Rad;
            float cosLat = Mathf.Cos(lat);
            return new Vector3(cosLat * Mathf.Sin(lon), Mathf.Sin(lat),
                -cosLat * Mathf.Cos(lon)).normalized * radius;
        }

        private void Cancel(string message)
        {
            working = false;
            confirmationPending = false;
            status = message;
            log.LogWarning("Equator fill: " + message);
        }
    }
}
