using Capture.Hook.Common;
using Capture.Interface;
using SharpDX;
using SharpDX.Direct3D9;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace Capture.Hook
{
    internal class DXHookD3D9 : BaseDXHook
    {
        public DXHookD3D9(CaptureInterface ssInterface)
            : base(ssInterface)
        {
        }

        /// <summary>
        /// The IDirect3DDevice9.EndScene function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_EndSceneDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_CreateQueryDelegate(IntPtr devicePtr, int Type1, IntPtr ppQuery);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_DrawIndexedPrimitiveDelegate(IntPtr devicePtr, PrimitiveType arg0, int baseVertexIndex, int minVertexIndex, int numVertices, int startIndex, int primCount);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_SetStreamSourceDelegate(IntPtr devicePtr, uint StreamNumber, IntPtr pStreamData, uint OffsetInBytes, uint sStride);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_SetTextureDelegate(IntPtr devicePtr, uint Sampler, IntPtr pTexture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_ResetDelegate(IntPtr device, ref PresentParameters presentParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        unsafe delegate int Direct3D9Device_PresentDelegate(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        unsafe delegate int Direct3D9DeviceEx_PresentExDelegate(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion, Present dwFlags);

        Hook<Direct3D9Device_EndSceneDelegate> Direct3DDevice_EndSceneHook = null;
        Hook<Direct3D9Device_ResetDelegate> Direct3DDevice_ResetHook = null;
        Hook<Direct3D9Device_PresentDelegate> Direct3DDevice_PresentHook = null;
        Hook<Direct3D9DeviceEx_PresentExDelegate> Direct3DDeviceEx_PresentExHook = null;
        Hook<Direct3D9Device_DrawIndexedPrimitiveDelegate> Direct3DDevice_DrawIndexedPrimitiveHook = null;
        Hook<Direct3D9Device_CreateQueryDelegate> Direct3DDevice_CreateQueryHook = null;
        Hook<Direct3D9Device_SetStreamSourceDelegate> Direct3DDevice_SetStreamSourceHook = null;
        Hook<Direct3D9Device_SetTextureDelegate> Direct3DDevice_SetTextureHook = null;

        object _lockRenderTarget = new object();

        bool _resourcesInitialised;
        Query _query;
        SharpDX.Direct3D9.Font _font;
        bool _queryIssued;
        ScreenshotRequest _requestCopy;
        bool _renderTargetCopyLocked = false;
        Surface _renderTargetCopy;
        Surface _resolvedTarget;

        protected override string HookName
        {
            get
            {
                return "DXHookD3D9";
            }
        }

        List<IntPtr> id3dDeviceFunctionAddresses = new List<IntPtr>();
        const int D3D9_DEVICE_METHOD_COUNT = 119;
        const int D3D9Ex_DEVICE_METHOD_COUNT = 15;
        bool _supportsDirect3D9Ex = false;
        public override void Hook()
        {
            this.DebugMessage("Hook: Begin");
            // First we need to determine the function address for IDirect3DDevice9
            Device device;
            id3dDeviceFunctionAddresses = new List<IntPtr>();
            //id3dDeviceExFunctionAddresses = new List<IntPtr>();
            this.DebugMessage("Hook: Before device creation");
            using (Direct3D d3d = new Direct3D())
            using (var renderForm = new System.Windows.Forms.Form())
            using (device = new Device(d3d, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing, new PresentParameters() { BackBufferWidth = 1, BackBufferHeight = 1, DeviceWindowHandle = renderForm.Handle }))
            {
                this.DebugMessage("Hook: Device created");
                id3dDeviceFunctionAddresses.AddRange(GetVTblAddresses(device.NativePointer, D3D9_DEVICE_METHOD_COUNT));
            }

            try
            {
                using (Direct3DEx d3dEx = new Direct3DEx())
                using (var renderForm = new System.Windows.Forms.Form())
                using (var deviceEx = new DeviceEx(d3dEx, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing, new PresentParameters() { BackBufferWidth = 1, BackBufferHeight = 1, DeviceWindowHandle = renderForm.Handle }, new DisplayModeEx() { Width = 800, Height = 600 }))
                {
                    this.DebugMessage("Hook: DeviceEx created - PresentEx supported");
                    id3dDeviceFunctionAddresses.AddRange(GetVTblAddresses(deviceEx.NativePointer, D3D9_DEVICE_METHOD_COUNT, D3D9Ex_DEVICE_METHOD_COUNT));
                    _supportsDirect3D9Ex = true;
                }
            }
            catch (Exception)
            {
                _supportsDirect3D9Ex = false;
            }

            // We want to hook each method of the IDirect3DDevice9 interface that we are interested in

            // 42 - EndScene (we will retrieve the back buffer here)
            Direct3DDevice_EndSceneHook = new Hook<Direct3D9Device_EndSceneDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.EndScene],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                // (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x1ce09),
                // A 64-bit app would use 0xff18
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_EndSceneDelegate(EndSceneHook),
                this);

            Direct3DDevice_CreateQueryHook = new Hook<Direct3D9Device_CreateQueryDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.CreateQuery],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                // (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x1ce09),
                // A 64-bit app would use 0xff18
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_CreateQueryDelegate(CreateQueryHook),
                this);

            // 42 - EndScene (we will retrieve the back buffer here)
            Direct3DDevice_DrawIndexedPrimitiveHook = new Hook<Direct3D9Device_DrawIndexedPrimitiveDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.DrawIndexedPrimitive],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                // (IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x1ce09),
                // A 64-bit app would use 0xff18
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_DrawIndexedPrimitiveDelegate(DrawIndexedPrimitiveHook),
                this);

            Direct3DDevice_SetStreamSourceHook = new Hook<Direct3D9Device_SetStreamSourceDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.SetStreamSource],
                new Direct3D9Device_SetStreamSourceDelegate(SetStreamSourceHook),
                this);


            Direct3DDevice_SetTextureHook = new Hook<Direct3D9Device_SetTextureDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.SetTexture],
                new Direct3D9Device_SetTextureDelegate(SetTextureHook),
                this);

            unsafe
            {
                // If Direct3D9Ex is available - hook the PresentEx
                if (_supportsDirect3D9Ex)
                {
                    Direct3DDeviceEx_PresentExHook = new Hook<Direct3D9DeviceEx_PresentExDelegate>(
                        id3dDeviceFunctionAddresses[(int)Direct3DDevice9ExFunctionOrdinals.PresentEx],
                        new Direct3D9DeviceEx_PresentExDelegate(PresentExHook),
                        this);
                }

                // Always hook Present also (device will only call Present or PresentEx not both)
                Direct3DDevice_PresentHook = new Hook<Direct3D9Device_PresentDelegate>(
                    id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Present],
                    new Direct3D9Device_PresentDelegate(PresentHook),
                    this);
            }

            // 16 - Reset (called on resolution change or windowed/fullscreen change - we will reset some things as well)
            Direct3DDevice_ResetHook = new Hook<Direct3D9Device_ResetDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.Reset],
                // On Windows 7 64-bit w/ 32-bit app and d3d9 dll version 6.1.7600.16385, the address is equiv to:
                //(IntPtr)(GetModuleHandle("d3d9").ToInt32() + 0x58dda),
                // A 64-bit app would use 0x3b3a0
                // Note: GetD3D9DeviceFunctionAddress will output these addresses to a log file
                new Direct3D9Device_ResetDelegate(ResetHook),
                this);

            /*
             * Don't forget that all hooks will start deactivated...
             * The following ensures that all threads are intercepted:
             * Note: you must do this for each hook.
             */

            Direct3DDevice_EndSceneHook.Activate();
            Hooks.Add(Direct3DDevice_EndSceneHook);

            //weilh
            Direct3DDevice_DrawIndexedPrimitiveHook.Activate();
            Hooks.Add(Direct3DDevice_DrawIndexedPrimitiveHook);

            Direct3DDevice_PresentHook.Activate();
            Hooks.Add(Direct3DDevice_PresentHook);

            Direct3DDevice_CreateQueryHook.Activate();
            Hooks.Add(Direct3DDevice_CreateQueryHook);

            Direct3DDevice_SetStreamSourceHook.Activate();
            Hooks.Add(Direct3DDevice_SetStreamSourceHook);

            Direct3DDevice_SetTextureHook.Activate();
            Hooks.Add(Direct3DDevice_SetTextureHook);

            if (_supportsDirect3D9Ex)
            {
                Direct3DDeviceEx_PresentExHook.Activate();
                Hooks.Add(Direct3DDeviceEx_PresentExHook);
            }

            Direct3DDevice_ResetHook.Activate();
            Hooks.Add(Direct3DDevice_ResetHook);

            this.DebugMessage("Hook: End");
        }

        /// <summary>
        /// Just ensures that the surface we created is cleaned up.
        /// </summary>
        public override void Cleanup()
        {
            lock (_lockRenderTarget)
            {
                _resourcesInitialised = false;

                RemoveAndDispose(ref _renderTargetCopy);
                _renderTargetCopyLocked = false;

                RemoveAndDispose(ref _resolvedTarget);
                RemoveAndDispose(ref _query);
                _queryIssued = false;

                RemoveAndDispose(ref _font);

                RemoveAndDispose(ref _overlayEngine);
            }
            if (Direct3DDevice_EndSceneHook != null)
            {
                Direct3DDevice_EndSceneHook.Dispose();
            }
            if (Direct3DDevice_ResetHook != null)
            {
                Direct3DDevice_ResetHook.Dispose();
            }
            if (Direct3DDevice_PresentHook != null)
            {
                Direct3DDevice_PresentHook.Dispose();
            }
            if (Direct3DDeviceEx_PresentExHook != null)
            {
                Direct3DDeviceEx_PresentExHook.Dispose();
            }
            if (Direct3DDevice_DrawIndexedPrimitiveHook != null)
            {
                Direct3DDevice_DrawIndexedPrimitiveHook.Dispose();
            }
            if (Direct3DDevice_CreateQueryHook != null)
            {
                Direct3DDevice_CreateQueryHook.Dispose();
            }
            if (Direct3DDevice_SetStreamSourceHook != null)
            {
                Direct3DDevice_SetStreamSourceHook.Dispose();
            }
            if (Direct3DDevice_SetTextureHook != null)
            {
                Direct3DDevice_SetTextureHook.Dispose();
            }
        }

        private uint stride;
        private int numVertices;
        private int primCount;
        private int vSize;
        private bool initOnce = true;

        private bool _isUsingPresent = false;

        private SharpDX.Mathematics.Interop.RawViewport Viewport;

        private static class Configure
        {
            /// <summary>
            /// 是否开启透视
            /// </summary>
            public static bool WallhackEnabled => true;

            /// <summary>
            /// 是否开启武器透视
            /// </summary>
            public static bool WeaponEnabled => true;

            /// <summary>
            /// 是否开启透视（Texture方法）
            /// </summary>
            public static bool WallhackInTextureEnabled => false;

            /// <summary>
            /// 是否放大头部
            /// </summary>
            public static bool ZoomhackEnabled => false;
        }

        private struct WeaponEspInfo
        {
            public float pOutX;
            public float pOutY;
            public float RealDistance;
        };
        private List<WeaponEspInfo> WeaponEspInfoList = new List<WeaponEspInfo>();

        private int CreateQueryHook(IntPtr devicePtr, int Type, IntPtr ppQuery)
        {
            if (Type == 9)
            {
                Type = 10;
            }

            return Direct3DDevice_CreateQueryHook.Original(devicePtr, Type, ppQuery);
        }

        private int DrawIndexedPrimitiveHook(IntPtr devicePtr, PrimitiveType arg0, int baseVertexIndex, int minVertexIndex, int numVertices, int startIndex, int primCount)
        {
            this.primCount = primCount;
            this.numVertices = numVertices;

            var device = new Device(devicePtr);
            if (device != null)
            {
                if (IsPlayers(stride, vSize, numVertices, primCount))
                {
                    device.SetRenderState(RenderState.Lighting, false);
                    device.SetRenderState(RenderState.ZEnable, false);
                    device.SetRenderState(RenderState.FillMode, FillMode.Solid);
                    //设置墙后颜色
                    device.SetTexture(0, textureBack);
                    Direct3DDevice_DrawIndexedPrimitiveHook.Original(devicePtr, arg0, baseVertexIndex, minVertexIndex, numVertices, startIndex, primCount);

                    device.SetRenderState(RenderState.ZEnable, true);
                    device.SetTexture(0, textureFront);
                    Direct3DDevice_DrawIndexedPrimitiveHook.Original(devicePtr, arg0, baseVertexIndex, minVertexIndex, numVertices, startIndex, primCount);
                }
                else
                {
                    Direct3DDevice_DrawIndexedPrimitiveHook.Original(devicePtr, arg0, baseVertexIndex, minVertexIndex, numVertices, startIndex, primCount);
                }
            }
            return 1;
        }

        private int SetStreamSourceHook(IntPtr devicePtr, uint StreamNumber, IntPtr pStreamData, uint OffsetInBytes, uint sStride)
        {
            if (StreamNumber == 0)
            {
                this.stride = sStride;
            }
            return Direct3DDevice_SetStreamSourceHook.Original(devicePtr, StreamNumber, pStreamData, OffsetInBytes, sStride);
        }

        private int SetTextureHook(IntPtr devicePtr, uint Sampler, IntPtr pTexture)
        {
            var device = new Device(devicePtr);
            if (initOnce)
            {
                initOnce = false;

                this.Viewport = device.Viewport;
            }

            var vShader = device.VertexShader;
            if (vShader != null)
            {
                if (vShader.Function.BufferSize != null)
                {
                    this.vSize = vShader.Function.BufferSize;
                    vShader.Function.Dispose();
                }
                vShader.Dispose();
            }

            if (Configure.WallhackInTextureEnabled)
            {
                if (IsPlayers(this.stride, this.vSize, this.numVertices, this.primCount))
                {
                    //AddWeapons(device);

                    device.SetTexture(0, textureBack);
                    device.SetRenderState(RenderState.ZEnable, false);
                    Direct3DDevice_DrawIndexedPrimitiveHook.Original(devicePtr, PrimitiveType.TriangleList, 0, 0, numVertices, 0, primCount);
                    device.SetRenderState(RenderState.ZEnable, true);
                    device.SetTexture(0, textureFront);
                }
            }

            //draw square on heads
            if (Configure.ZoomhackEnabled)
            {
                int pSize = 0;
                if (device.PixelShader != null)
                {
                    pSize = device.PixelShader.Function.BufferSize;
                    device.PixelShader.Function.Dispose();
                    device.PixelShader.Dispose();
                }

                int numElements = 0;
                if (device.VertexDeclaration != null)
                {
                    numElements = device.VertexDeclaration.Elements.Length;
                    device.VertexDeclaration.Dispose();
                }

                var pLockedRect = default(DataRectangle);
                var qCRC = default(uint);
                if (numElements == 6 && Sampler == 0 && pTexture != null)
                {
                    var texture = new Texture(pTexture);
                    var sDesc = texture.GetLevelDescription(0);

                    if (sDesc.Pool == Pool.Managed && texture.TypeInfo == ResourceType.Texture && sDesc.Width == 512 && sDesc.Height == 512)
                    {
                        pLockedRect = texture.LockRectangle(0, LockFlags.DoNotWait | LockFlags.ReadOnly | LockFlags.NoSystemLock);
                        qCRC = QuickChecksum(pLockedRect.DataPointer, pLockedRect.Pitch);
                        texture.UnlockRectangle(0);
                    }
                }

                if ((stride == 36 && vSize == 2356 && pSize != 1848 && pLockedRect.Pitch == 2048 && numElements == 6) || //hair	
                    (stride == 44 && vSize == 2356 && pSize == 2272 && pLockedRect.Pitch == 1024 && numElements == 10) || //hair2
                    (vSize == 2008 && qCRC == 0xc46ee841) ||//helmet 1
                    (vSize == 2008 && qCRC == 0x9590d282) ||//helmet 2
                    (vSize == 2008 && qCRC == 0xe248e914))//helmet 3
                {
                    device.SetRenderState(RenderState.DepthBias, 0);
                    device.SetRenderState(RenderState.FillMode, FillMode.Point);
                    DrawtoTarget(device);
                }
            }

            return Direct3DDevice_SetTextureHook.Original(devicePtr, Sampler, pTexture);
        }

        private static ICollection<String> players = new List<String>();
        private static ISet<uint> guns = new HashSet<uint> { 20, 24, 28, 32, 36, 40, 56, 80 };

        private bool IsPlayers(uint stride, int vSize, int numVertices, int primCount)
        {
            if (this.Config.WallhackEnabled && stride == 72)
            {
                //var player = $"{numVertices}, {primCount}";
                //if (!players.Contains(player))
                //{
                //    this.DebugMessage(player);
                //    players.Add(player);
                //}

                if ((numVertices == 1242 && primCount == 1116) || (numVertices == 4195 && primCount == 4548) || (numVertices == 1182 && primCount == 1940) || (numVertices == 1030 && primCount == 1768) || (numVertices == 2020 && primCount == 3196) || (numVertices == 1694 && primCount == 2812) || (numVertices == 1924 && primCount == 3436) || (numVertices == 356 && primCount == 534) || (numVertices == 116 && primCount == 164) || (numVertices == 4031 && primCount == 7302) || (numVertices == 4193 && primCount == 5570) || (numVertices == 172 && primCount == 268) || (numVertices == 1337 && primCount == 2376) || (numVertices == 1190 && primCount == 2158) || (numVertices == 3075 && primCount == 5528) || (numVertices == 2930 && primCount == 5266) || (numVertices == 4764 && primCount == 7618) || (numVertices == 4971 && primCount == 3958) || (numVertices == 3178 && primCount == 5570) || (numVertices == 7430 && primCount == 11990) || (numVertices == 5242 && primCount == 5145) || (numVertices == 2695 && primCount == 4710) || (numVertices == 1705 && primCount == 3076) || (numVertices == 1149 && primCount == 1418) || (numVertices == 262 && primCount == 326) || (numVertices == 2274 && primCount == 3557) || (numVertices == 1379 && primCount == 2318) || (numVertices == 834 && primCount == 1378) || (numVertices == 490 && primCount == 664) || (numVertices == 1326 && primCount == 2216) || (numVertices == 2140 && primCount == 3736) || (numVertices == 1626 && primCount == 2716) || (numVertices == 1908 && primCount == 3160) || (numVertices == 1354 && primCount == 2202) || (numVertices == 1801 && primCount == 1458) || (numVertices == 1392 && primCount == 2364) || (numVertices == 1883 && primCount == 1530) || (numVertices == 645 && primCount == 1062) || (numVertices == 530 && primCount == 880) || (numVertices == 4489 && primCount == 6816) || (numVertices == 2114 && primCount == 3018) || (numVertices == 5552 && primCount == 8770) || (numVertices == 1681 && primCount == 2748) || (numVertices == 10108 && primCount == 11216) || (numVertices == 3562 && primCount == 6396) || (numVertices == 1366 && primCount == 2360) || (numVertices == 6812 && primCount == 6885) || (numVertices == 1460 && primCount == 2464) || (numVertices == 5674 && primCount == 6826) || (numVertices == 3460 && primCount == 5640) || (numVertices == 3143 && primCount == 4656) || (numVertices == 4997 && primCount == 8162) || (numVertices == 4323 && primCount == 6961) || (numVertices == 2474 && primCount == 4264) || (numVertices == 7629 && primCount == 9067) || (numVertices == 477 && primCount == 748) || (numVertices == 467 && primCount == 752) || (numVertices == 860 && primCount == 1194) || (numVertices == 2037 && primCount == 3187) || (numVertices == 788 && primCount == 1156) || (numVertices == 866 && primCount == 1518) || (numVertices == 4545 && primCount == 5300) || (numVertices == 1755 && primCount == 2824) || (numVertices == 2092 && primCount == 3172) || (numVertices == 144 && primCount == 216) || (numVertices == 621 && primCount == 986) || (numVertices == 250 && primCount == 370) || (numVertices == 4478 && primCount == 4127) || (numVertices == 2498 && primCount == 3664) || (numVertices == 1930 && primCount == 3464) || (numVertices == 2651 && primCount == 4790) || (numVertices == 684 && primCount == 1092) || (numVertices == 1716 && primCount == 2840) || (numVertices == 1684 && primCount == 2870) || (numVertices == 194 && primCount == 352))
                {
                    return true;
                }
            }
            else if (this.Config.WeaponEnabled && guns.Contains(stride))
            {
                if ((stride == 36 && numVertices == 80 && primCount == 74) || (stride == 32 && numVertices == 328 && primCount == 308) || (stride == 24 && numVertices == 435 && primCount == 419) || (stride == 32 && numVertices == 2866 && primCount == 2615) || (stride == 32 && numVertices == 1427 && primCount == 1214) || (stride == 36 && numVertices == 190 && primCount == 188) || (stride == 56 && numVertices == 367 && primCount == 371) || (stride == 36 && numVertices == 1097 && primCount == 890) || (stride == 36 && numVertices == 2679 && primCount == 3049) || (stride == 32 && numVertices == 110 && primCount == 94) || (stride == 32 && numVertices == 230 && primCount == 262) || (stride == 24 && numVertices == 184 && primCount == 156) || (stride == 56 && numVertices == 346 && primCount == 290) || (stride == 32 && numVertices == 1982 && primCount == 2239) || (stride == 32 && numVertices == 1071 && primCount == 1079) || (stride == 36 && numVertices == 76 && primCount == 66) || (stride == 36 && numVertices == 502 && primCount == 479) || (stride == 56 && numVertices == 782 && primCount == 660) || (stride == 56 && numVertices == 560 && primCount == 502) || (stride == 36 && numVertices == 166 && primCount == 198) || (stride == 36 && numVertices == 6094 && primCount == 5227) || (stride == 36 && numVertices == 98 && primCount == 82) || (stride == 56 && numVertices == 919 && primCount == 961) || (stride == 36 && numVertices == 2692 && primCount == 2715) || (stride == 36 && numVertices == 3040 && primCount == 4145) || (stride == 36 && numVertices == 124 && primCount == 116) || (stride == 36 && numVertices == 460 && primCount == 576) || (stride == 32 && numVertices == 122 && primCount == 140) || (stride == 56 && numVertices == 531 && primCount == 491) || (stride == 56 && numVertices == 992 && primCount == 1152) || (stride == 36 && numVertices == 6795 && primCount == 7362) || (stride == 36 && numVertices == 472 && primCount == 644) || (stride == 36 && numVertices == 70 && primCount == 62) || (stride == 36 && numVertices == 634 && primCount == 650) || (stride == 36 && numVertices == 676 && primCount == 634) || (stride == 28 && numVertices == 447 && primCount == 468) || (stride == 24 && numVertices == 476 && primCount == 458) || (stride == 36 && numVertices == 5692 && primCount == 5571) || (stride == 24 && numVertices == 1731 && primCount == 1680) || (stride == 32 && numVertices == 70 && primCount == 62) || (stride == 32 && numVertices == 94 && primCount == 124) || (stride == 32 && numVertices == 32 && primCount == 28) || (stride == 32 && numVertices == 581 && primCount == 624) || (stride == 24 && numVertices == 496 && primCount == 492) || (stride == 24 && numVertices == 278 && primCount == 284) || (stride == 32 && numVertices == 10123 && primCount == 8841) || (stride == 24 && numVertices == 1846 && primCount == 1920) || (stride == 32 && numVertices == 69 && primCount == 72) || (stride == 32 && numVertices == 244 && primCount == 208) || (stride == 32 && numVertices == 4 && primCount == 2) || (stride == 32 && numVertices == 124 && primCount == 102) || (stride == 32 && numVertices == 140 && primCount == 90) || (stride == 32 && numVertices == 142 && primCount == 112) || (stride == 24 && numVertices == 300 && primCount == 274) || (stride == 32 && numVertices == 4584 && primCount == 4478) || (stride == 24 && numVertices == 1114 && primCount == 1063) || (stride == 32 && numVertices == 72 && primCount == 62) || (stride == 32 && numVertices == 598 && primCount == 562) || (stride == 32 && numVertices == 102 && primCount == 102) || (stride == 32 && numVertices == 132 && primCount == 124) || (stride == 24 && numVertices == 548 && primCount == 470) || (stride == 24 && numVertices == 687 && primCount == 710) || (stride == 32 && numVertices == 9871 && primCount == 8639) || (stride == 24 && numVertices == 1354 && primCount == 1226) || (stride == 36 && numVertices == 116 && primCount == 106) || (stride == 36 && numVertices == 628 && primCount == 782) || (stride == 36 && numVertices == 686 && primCount == 810) || (stride == 24 && numVertices == 2545 && primCount == 2708) || (stride == 24 && numVertices == 40 && primCount == 62) || (stride == 24 && numVertices == 510 && primCount == 472) || (stride == 36 && numVertices == 5462 && primCount == 6362) || (stride == 36 && numVertices == 1296 && primCount == 1898) || (stride == 36 && numVertices == 82 && primCount == 70) || (stride == 24 && numVertices == 934 && primCount == 1104) || (stride == 36 && numVertices == 7830 && primCount == 8285) || (stride == 32 && numVertices == 2726 && primCount == 3060) || (stride == 32 && numVertices == 74 && primCount == 78) || (stride == 32 && numVertices == 398 && primCount == 356) || (stride == 32 && numVertices == 838 && primCount == 796) || (stride == 32 && numVertices == 1938 && primCount == 1496) || (stride == 24 && numVertices == 1346 && primCount == 1568) || (stride == 24 && numVertices == 524 && primCount == 536) || (stride == 32 && numVertices == 10255 && primCount == 9897) || (stride == 80 && numVertices == 76 && primCount == 66) || (stride == 80 && numVertices == 148 && primCount == 118) || (stride == 80 && numVertices == 103 && primCount == 104) || (stride == 28 && numVertices == 386 && primCount == 400) || (stride == 80 && numVertices == 7223 && primCount == 6775) || (stride == 80 && numVertices == 4512 && primCount == 4614) || (stride == 32 && numVertices == 148 && primCount == 130) || (stride == 32 && numVertices == 681 && primCount == 836) || (stride == 32 && numVertices == 46 && primCount == 42) || (stride == 24 && numVertices == 591 && primCount == 530) || (stride == 32 && numVertices == 11037 && primCount == 9965) || (stride == 24 && numVertices == 2028 && primCount == 1716) || (stride == 28 && numVertices == 941 && primCount == 783) || (stride == 28 && numVertices == 98 && primCount == 94) || (stride == 24 && numVertices == 377 && primCount == 358) || (stride == 28 && numVertices == 1336 && primCount == 1332) || (stride == 28 && numVertices == 101 && primCount == 83) || (stride == 28 && numVertices == 6616 && primCount == 5978) || (stride == 36 && numVertices == 2353 && primCount == 2399) || (stride == 36 && numVertices == 184 && primCount == 212) || (stride == 36 && numVertices == 56 && primCount == 50) || (stride == 24 && numVertices == 680 && primCount == 680) || (stride == 56 && numVertices == 890 && primCount == 784) || (stride == 56 && numVertices == 477 && primCount == 496) || (stride == 36 && numVertices == 5355 && primCount == 5870) || (stride == 28 && numVertices == 1442 && primCount == 1608) || (stride == 36 && numVertices == 70 && primCount == 60) || (stride == 36 && numVertices == 173 && primCount == 210) || (stride == 36 && numVertices == 16 && primCount == 12) || (stride == 28 && numVertices == 537 && primCount == 618) || (stride == 24 && numVertices == 643 && primCount == 574) || (stride == 36 && numVertices == 6807 && primCount == 7167) || (stride == 80 && numVertices == 106 && primCount == 98) || (stride == 80 && numVertices == 221 && primCount == 163) || (stride == 80 && numVertices == 7 && primCount == 5) || (stride == 24 && numVertices == 868 && primCount == 936) || (stride == 80 && numVertices == 8184 && primCount == 7364) || (stride == 28 && numVertices == 3483 && primCount == 3020) || (stride == 40 && numVertices == 137 && primCount == 134) || (stride == 40 && numVertices == 114 && primCount == 82) || (stride == 24 && numVertices == 868 && primCount == 952) || (stride == 40 && numVertices == 9666 && primCount == 10299) || (stride == 36 && numVertices == 116 && primCount == 104) || (stride == 36 && numVertices == 180 && primCount == 167) || (stride == 24 && numVertices == 655 && primCount == 600) || (stride == 24 && numVertices == 1129 && primCount == 914) || (stride == 24 && numVertices == 177 && primCount == 178) || (stride == 36 && numVertices == 15579 && primCount == 15008) || (stride == 24 && numVertices == 820 && primCount == 687) || (stride == 28 && numVertices == 3485 && primCount == 3020) || (stride == 40 && numVertices == 146 && primCount == 122) || (stride == 40 && numVertices == 145 && primCount == 158) || (stride == 28 && numVertices == 1800 && primCount == 1810) || (stride == 24 && numVertices == 731 && primCount == 723) || (stride == 24 && numVertices == 700 && primCount == 720) || (stride == 40 && numVertices == 8379 && primCount == 6826) || (stride == 28 && numVertices == 1226 && primCount == 1177) || (stride == 40 && numVertices == 94 && primCount == 82) || (stride == 32 && numVertices == 388 && primCount == 356) || (stride == 56 && numVertices == 600 && primCount == 548) || (stride == 40 && numVertices == 273 && primCount == 289) || (stride == 40 && numVertices == 8237 && primCount == 8326) || (stride == 28 && numVertices == 898 && primCount == 974) || (stride == 36 && numVertices == 86 && primCount == 76) || (stride == 36 && numVertices == 154 && primCount == 141) || (stride == 28 && numVertices == 906 && primCount == 996) || (stride == 24 && numVertices == 2746 && primCount == 2890) || (stride == 24 && numVertices == 850 && primCount == 922) || (stride == 56 && numVertices == 259 && primCount == 238) || (stride == 56 && numVertices == 496 && primCount == 492) || (stride == 36 && numVertices == 10260 && primCount == 9952) || (stride == 24 && numVertices == 629 && primCount == 534))
                {
                    return true;
                }
            }

            return false;
        }

        private int EndSceneHook(IntPtr devicePtr)
        {
            Device device = (Device)devicePtr;
            if (!_isUsingPresent)
                DoCaptureRenderTarget(device, "EndSceneHook");
            if (line != null)
            {
                line.Draw(rawVector2s.ToArray(), new SharpDX.Mathematics.Interop.RawColorBGRA(0, 255, 0, 15));
            }
            WeaponEspInfoList.Clear();
            //SharpDX.Direct3D9.Font font = new SharpDX.Direct3D9.Font(device, new FontDescription()
            //{
            //    Height = 16,
            //    FaceName = "Arial",
            //    Italic = false,
            //    Width = 0,
            //    MipLevels = 1,
            //    CharacterSet = FontCharacterSet.Default,
            //    OutputPrecision = FontPrecision.Default,
            //    Quality = FontQuality.Antialiased,
            //    PitchAndFamily = FontPitchAndFamily.Default | FontPitchAndFamily.DontCare,
            //    Weight = FontWeight.Bold
            //});

            //font.DrawText(null, "开启成功", 50, 50, SharpDX.Color.Green);
            //font.Dispose();
            return Direct3DDevice_EndSceneHook.Original(devicePtr);
        }

        private int ResetHook(IntPtr devicePtr, ref PresentParameters presentParameters)
        {
            // Ensure certain overlay resources have performed necessary pre-reset tasks
            if (_overlayEngine != null)
                _overlayEngine.BeforeDeviceReset();

            Cleanup();

            return Direct3DDevice_ResetHook.Original(devicePtr, ref presentParameters);
        }

        private unsafe int PresentHook(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion)
        {
            _isUsingPresent = true;
            Device device = (Device)devicePtr;

            DoCaptureRenderTarget(device, "PresentHook");
            SetColor(devicePtr, 1);

            return Direct3DDevice_PresentHook.Original(devicePtr, pSourceRect, pDestRect, hDestWindowOverride, pDirtyRegion);
        }

        private unsafe int PresentExHook(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion, Present dwFlags)
        {
            _isUsingPresent = true;
            DeviceEx device = (DeviceEx)devicePtr;

            DoCaptureRenderTarget(device, "PresentEx");
            SetColor(devicePtr, 0);

            return Direct3DDeviceEx_PresentExHook.Original(devicePtr, pSourceRect, pDestRect, hDestWindowOverride, pDirtyRegion, dwFlags);
        }

        private void AddWeapons(Device device)
        {
            try
            {
                //var floats = device.GetVertexShaderFloatConstant(0, 4);
                //var floatN = new float[16];
                //floatN[3] = floats[0];
                //floatN[7] = floats[1];
                //floatN[11] = floats[2];
                //floatN[15] = floats[3];
                //var matrix = new SharpDX.Matrix(floatN);
                //SharpDX.Vector3 pOut;
                //SharpDX.Vector3 pIn = new SharpDX.Vector3(0, 3, 0);
                //float distance = pIn.X * matrix.M14 + pIn.Y * matrix.M24 + pIn.Z * matrix.M34 + matrix.M44;
                //matrix = SharpDX.Matrix.Transpose(matrix);
                //pOut = SharpDX.Vector3.TransformCoordinate(pIn, matrix);

                //pOut.X = Viewport.X + (1.0f + pOut.X) * Viewport.Width / 2.0f;
                //pOut.Y = Viewport.Y + (1.0f - pOut.Y) * Viewport.Height / 2.0f;
                //float x1, y1;
                //if (pOut.X > 0.0f && pOut.Y > 0.0f && pOut.X < Viewport.Width && pOut.Y < Viewport.Height)
                //{
                //    x1 = pOut.X;
                //    y1 = pOut.Y;

                //}
                //else
                //{
                //    x1 = -1.0f;
                //    y1 = -1.0f;
                //}



                //this.DebugMessage(pOut.X + "," + pOut.Y + "," + distance);
                //WeaponEspInfo pWeaponEspInfo = new WeaponEspInfo { pOutX = x1, pOutY = y1, RealDistance = distance };
                //WeaponEspInfoList.Add(pWeaponEspInfo);
            }
            catch (Exception ex)
            {
                this.DebugMessage(ex.Message);
            }
        }

        private void DrawtoTarget(Device device)
        {
            float pointSize = 5.0f;
            device.SetRenderState(RenderState.PointSpriteEnable, false);
            device.SetRenderState(RenderState.PointScaleEnable, false);
            device.SetRenderState(RenderState.PointSize, pointSize);
            device.SetRenderState(RenderState.PointSizeMax, pointSize);
            device.SetRenderState(RenderState.PointSizeMin, pointSize);
            device.SetRenderState(RenderState.StencilEnable, false);
            //device.SetRenderState(RenderState.PointScaleA, false);
            device.SetRenderState(RenderState.StencilEnable, true);
        }

        private unsafe uint QuickChecksum(IntPtr pData, int size)
        {
            uint* point = (uint*)pData.ToPointer();
            uint sum;
            uint tmp;
            sum = *point;

            for (int i = 1; i < (size / 4); i++)
            {
                tmp = point[i];
                tmp = (sum >> 29) + tmp;
                tmp = (sum >> 17) + tmp;
                sum = (sum << 3) ^ tmp;
            }

            return sum;
        }

        void SetColor(IntPtr devicePtr, int isEx)
        {
            if (!isFirstHook)
            {
                try
                {
                    isFirstHook = true;
                    this.DebugMessage("加载纯色纹理");
                    int _texWidth = 1, _texHeight = 1;
                    Bitmap bmFront = new Bitmap(_texWidth, _texHeight);
                    Graphics gFront = Graphics.FromImage(bmFront); //创建b1的Graphics
                    gFront.FillRectangle(Brushes.Green, new System.Drawing.Rectangle(0, 0, _texWidth, _texHeight));
                    string fileNameFront = "..//Front.jpg";
                    bmFront.Save(fileNameFront);

                    Bitmap bmBack = new Bitmap(_texWidth, _texHeight);
                    Graphics gBack = Graphics.FromImage(bmBack); //创建b1的Graphics
                    gBack.FillRectangle(Brushes.Gold, new System.Drawing.Rectangle(0, 0, _texWidth, _texHeight));
                    MemoryStream streamBack = new MemoryStream();
                    string fileNameBack = "..//back.jpg";
                    bmBack.Save(fileNameBack);


                    if (isEx == 0)
                    {
                        textureBack = Texture.FromFile((DeviceEx)devicePtr, fileNameBack);
                        textureFront = Texture.FromFile((DeviceEx)devicePtr, fileNameFront);

                    }
                    else
                    {
                        textureBack = Texture.FromFile((DeviceEx)devicePtr, fileNameBack);
                        textureFront = Texture.FromFile((DeviceEx)devicePtr, fileNameFront);
                    }
                }
                catch (Exception e)
                {
                    this.DebugMessage("error 330");
                    this.DebugMessage(e.Message);
                    if (e.InnerException != null)
                        this.DebugMessage(e.InnerException.Message);
                }

            }


            //if (isLine)
            //{
            //    line = new Line(device);
            //    isLine = false;
            //    var view = device.Viewport;
            //    int Vx = view.Width / 2;
            //    int Vy = view.Height / 2;
            //    for (int i = 0; i < 20; i++)
            //    {
            //        rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx, Vy + i));
            //        rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx, Vy - i));
            //        rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx + i, Vy));
            //        rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx - i, Vy));
            //    }
            //    line.Width = 1;
            //}
        }

        Line line;
        List<SharpDX.Mathematics.Interop.RawVector2> rawVector2s = new List<SharpDX.Mathematics.Interop.RawVector2>();
        bool isLine = true;

        DX9.DXOverlayEngine _overlayEngine;

        void DoCaptureRenderTarget(Device device, string hook)
        {
            this.Frame();

            try
            {
                #region Screenshot Request

                // If we have issued the command to copy data to our render target, check if it is complete
                bool qryResult;
                if (_queryIssued && _requestCopy != null && _query.GetData(out qryResult, false))
                {
                    // The GPU has finished copying data to _renderTargetCopy, we can now lock
                    // the data and access it on another thread.

                    _queryIssued = false;

                    // Lock the render target
                    SharpDX.Rectangle rect;
                    SharpDX.DataRectangle lockedRect = LockRenderTarget(_renderTargetCopy, out rect);
                    _renderTargetCopyLocked = true;

                    // Copy the data from the render target
                    System.Threading.Tasks.Task.Factory.StartNew(() =>
                    {
                        lock (_lockRenderTarget)
                        {
                            ProcessCapture(rect.Width, rect.Height, lockedRect.Pitch, _renderTargetCopy.Description.Format.ToPixelFormat(), lockedRect.DataPointer, _requestCopy);
                        }
                    });
                }

                // Single frame capture request
                if (this.Request != null)
                {
                    DateTime start = DateTime.Now;
                    try
                    {
                        using (Surface renderTarget = device.GetRenderTarget(0))
                        {
                            int width, height;

                            // If resizing of the captured image, determine correct dimensions
                            if (Request.Resize != null && (renderTarget.Description.Width > Request.Resize.Value.Width || renderTarget.Description.Height > Request.Resize.Value.Height))
                            {
                                if (renderTarget.Description.Width > Request.Resize.Value.Width)
                                {
                                    width = Request.Resize.Value.Width;
                                    height = (int)Math.Round((renderTarget.Description.Height * ((double)Request.Resize.Value.Width / (double)renderTarget.Description.Width)));
                                }
                                else
                                {
                                    height = Request.Resize.Value.Height;
                                    width = (int)Math.Round((renderTarget.Description.Width * ((double)Request.Resize.Value.Height / (double)renderTarget.Description.Height)));
                                }
                            }
                            else
                            {
                                width = renderTarget.Description.Width;
                                height = renderTarget.Description.Height;
                            }

                            // If existing _renderTargetCopy, ensure that it is the correct size and format
                            if (_renderTargetCopy != null && (_renderTargetCopy.Description.Width != width || _renderTargetCopy.Description.Height != height || _renderTargetCopy.Description.Format != renderTarget.Description.Format))
                            {
                                // Cleanup resources
                                Cleanup();
                            }

                            // Ensure that we have something to put the render target data into
                            if (!_resourcesInitialised || _renderTargetCopy == null)
                            {
                                CreateResources(device, width, height, renderTarget.Description.Format);
                            }

                            // Resize from render target Surface to resolvedSurface (also deals with resolving multi-sampling)
                            device.StretchRectangle(renderTarget, _resolvedTarget, TextureFilter.None);
                        }

                        // If the render target is locked from a previous request unlock it
                        if (_renderTargetCopyLocked)
                        {
                            // Wait for the the ProcessCapture thread to finish with it
                            lock (_lockRenderTarget)
                            {
                                if (_renderTargetCopyLocked)
                                {
                                    _renderTargetCopy.UnlockRectangle();
                                    _renderTargetCopyLocked = false;
                                }
                            }
                        }

                        // Copy data from resolved target to our render target copy
                        device.GetRenderTargetData(_resolvedTarget, _renderTargetCopy);

                        _requestCopy = Request.Clone();
                        _query.Issue(Issue.End);
                        _queryIssued = true;

                    }
                    finally
                    {
                        // We have completed the request - mark it as null so we do not continue to try to capture the same request
                        // Note: If you are after high frame rates, consider implementing buffers here to capture more frequently
                        //         and send back to the host application as needed. The IPC overhead significantly slows down 
                        //         the whole process if sending frame by frame.
                        Request = null;
                    }
                    DateTime end = DateTime.Now;
                    this.DebugMessage(hook + ": Capture time: " + (end - start).ToString());
                }

                #endregion

                var displayOverlays = Overlays;
                if (this.Config.ShowOverlay && displayOverlays != null)
                {
                    #region Draw Overlay

                    // Check if overlay needs to be initialised
                    if (_overlayEngine == null || _overlayEngine.Device.NativePointer != device.NativePointer
                        || IsOverlayUpdatePending)
                    {
                        // Cleanup if necessary
                        if (_overlayEngine != null)
                            RemoveAndDispose(ref _overlayEngine);

                        _overlayEngine = ToDispose(new DX9.DXOverlayEngine());
                        _overlayEngine.Overlays.AddRange((IEnumerable<IOverlay>)displayOverlays);
                        _overlayEngine.Initialise(device);
                        IsOverlayUpdatePending = false;
                    }
                    // Draw Overlay(s)
                    if (_overlayEngine != null)
                    {
                        foreach (var overlay in _overlayEngine.Overlays)
                            overlay.Frame();
                        _overlayEngine.Draw();
                    }

                    #endregion
                }
            }
            catch (Exception e)
            {
                DebugMessage(e.ToString());
            }
        }

        private SharpDX.DataRectangle LockRenderTarget(Surface _renderTargetCopy, out SharpDX.Rectangle rect)
        {
            if (_requestCopy.RegionToCapture.Height > 0 && _requestCopy.RegionToCapture.Width > 0)
            {
                rect = new SharpDX.Rectangle(_requestCopy.RegionToCapture.Left, _requestCopy.RegionToCapture.Top, _requestCopy.RegionToCapture.Width, _requestCopy.RegionToCapture.Height);
            }
            else
            {
                rect = new SharpDX.Rectangle(0, 0, _renderTargetCopy.Description.Width, _renderTargetCopy.Description.Height);
            }
            return _renderTargetCopy.LockRectangle(rect, LockFlags.ReadOnly);
        }

        private void CreateResources(Device device, int width, int height, Format format)
        {
            if (_resourcesInitialised) return;
            _resourcesInitialised = true;

            // Create offscreen surface to use as copy of render target data
            _renderTargetCopy = ToDispose(Surface.CreateOffscreenPlain(device, width, height, format, Pool.SystemMemory));

            // Create our resolved surface (resizing if necessary and to resolve any multi-sampling)
            _resolvedTarget = ToDispose(Surface.CreateRenderTarget(device, width, height, format, MultisampleType.None, 0, false));

            _query = ToDispose(new Query(device, QueryType.Event));
        }
    }
}
