using System.Collections.Generic;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.BrowserProcess.FrameDelivery
{
    /// <summary>
    /// The open shared frame regions, one per OSR browser, kept mapped across frames. Re-opening a
    /// region per frame would re-establish every page-table entry on each paint and give back most
    /// of what a persistent region exists to save, so a mapping is replaced only when the name
    /// changes — which is what a resize does — and released only when the browser goes away.
    ///
    /// <para>Not synchronised: CEF delivers process messages on the render process's main thread,
    /// which is the only thread that resolves or releases here.</para>
    /// </summary>
    internal sealed class OsrRegionCache
    {
        private readonly Dictionary<int, SharedRegion> _regions = new Dictionary<int, SharedRegion>();

        /// <summary>
        /// The mapping for <paramref name="browserId"/> at <paramref name="mapName"/>, opening it if
        /// this is a name the cache has not seen. Null when the region does not exist or holds fewer
        /// than <paramref name="required"/> bytes — the name may be stale because a resize bumped it
        /// while a notify was in flight, and skipping that frame beats throwing at frame rate. A
        /// name that cannot be opened still evicts whatever was cached for that browser.
        /// </summary>
        public SharedRegion Resolve(int browserId, string mapName, long required)
        {
            if (_regions.TryGetValue(browserId, out var cached))
            {
                if (cached.Name == mapName) return cached;
                cached.Dispose();
                _regions.Remove(browserId);
            }

            var region = SharedRegion.OpenExisting(mapName, required);
            if (region == null) return null;

            _regions[browserId] = region;
            return region;
        }

        /// <summary>
        /// Release the mapping for a browser that is gone. False when there was nothing to release —
        /// a browser destroyed before it ever painted, or one released twice.
        /// </summary>
        public bool Release(int browserId)
        {
            if (!_regions.TryGetValue(browserId, out var region)) return false;

            region.Dispose();
            _regions.Remove(browserId);
            return true;
        }
    }
}
