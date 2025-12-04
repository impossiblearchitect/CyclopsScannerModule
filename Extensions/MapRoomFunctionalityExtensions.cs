using UnityEngine;

namespace CyclopsScannerModule.Extensions;

public static class MapRoomFunctionalityExtensions
{
    // /// <summary>
    // /// Computes the 'intended' range of the MapRoomFunctionality by starting with the default range and adjusting for upgrades.
    // /// (This is necessary because the only obvious way to change the scale of the hologram UI involves changing the `scanRange` property.)
    // /// </summary>
    // public static float GetRange(this MapRoomFunctionality mrf)
    // {
    //     return MapRoomFunctionality.defaultRange + mrf.storageContainer.container.GetCount(TechType.MapRoomUpgradeScanRange)
    //         * MapRoomFunctionality.rangePerUpgrade;
    // }

    public static void SetScale(this MapRoomFunctionality mrf, float scale)
    {
        mrf.miniWorld.transform.localScale = scale * Vector3.one;
        mrf.scanRange = mrf.GetScanRange() * scale;
    }
    public static void Scale(this MapRoomFunctionality mrf, float scale)
    {
        mrf.miniWorld.transform.localScale *= scale;
        mrf.scanRange *= scale;
    }
}
