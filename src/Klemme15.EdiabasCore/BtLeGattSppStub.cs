// Stand-in for EdiabasLib's Windows-only Bluetooth LE serial adapter (BtLeGattSpp.cs, which needs WinRT).
// Klemme15 talks to K+DCAN cables over a serial port, so BLE adapters are simply reported as unavailable.
using System;
using System.IO;
using System.Threading;

namespace EdiabasLib
{
    public class BtLeGattSpp : IDisposable
    {
        public delegate void LogStringDelegate(string message);

        public BtLeGattSpp(LogStringDelegate logStringHandler = null) { }

        public Stream BtGattSppInStream => null;
        public Stream BtGattSppOutStream => null;

        public bool ConnectLeGattDevice(string deviceId, ManualResetEvent cancelEvent = null) => false;

        public void BtGattDisconnect() { }

        public void Dispose() { }
    }
}
