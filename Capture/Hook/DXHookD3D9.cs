using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
//using SlimDX.Direct3D9;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Capture.Hook.Common;
using Capture.Interface;
using SharpDX.Direct3D9;

namespace Capture.Hook
{
    internal class DXHookD3D9 : BaseDXHook
    {
        public DXHookD3D9(CaptureInterface ssInterface)
            : base(ssInterface)
        {
        }

        Hook<Direct3D9Device_EndSceneDelegate> Direct3DDevice_EndSceneHook = null;
        Hook<Direct3D9Device_ResetDelegate> Direct3DDevice_ResetHook = null;
        Hook<Direct3D9Device_PresentDelegate> Direct3DDevice_PresentHook = null;
        Hook<Direct3D9DeviceEx_PresentExDelegate> Direct3DDeviceEx_PresentExHook = null;
        //weilh
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
        //List<IntPtr> id3dDeviceExFunctionAddresses = new List<IntPtr>();
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
            {
                using (var renderForm = new System.Windows.Forms.Form())
                {
                    using (device = new Device(d3d, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing, new PresentParameters() { BackBufferWidth = 1, BackBufferHeight = 1, DeviceWindowHandle = renderForm.Handle, EnableAutoDepthStencil = true, AutoDepthStencilFormat = Format.D24S8 }))
                    {

                        this.DebugMessage("Hook: Device created");
                        id3dDeviceFunctionAddresses.AddRange(GetVTblAddresses(device.NativePointer, D3D9_DEVICE_METHOD_COUNT));
                    }
                }
            }

            try
            {
                using (Direct3DEx d3dEx = new Direct3DEx())
                {
                    this.DebugMessage("Hook: Direct3DEx...");
                    using (var renderForm = new System.Windows.Forms.Form())
                    {
                        using (var deviceEx = new DeviceEx(d3dEx, 0, DeviceType.NullReference, IntPtr.Zero, CreateFlags.HardwareVertexProcessing, new PresentParameters()
                        { BackBufferWidth = 1, BackBufferHeight = 1, DeviceWindowHandle = renderForm.Handle, EnableAutoDepthStencil = true, AutoDepthStencilFormat = Format.D24S8 }, new DisplayModeEx() { Width = 800, Height = 600 }))
                        {
                            this.DebugMessage("Hook: DeviceEx created - PresentEx supported");
                            id3dDeviceFunctionAddresses.AddRange(GetVTblAddresses(deviceEx.NativePointer, D3D9_DEVICE_METHOD_COUNT, D3D9Ex_DEVICE_METHOD_COUNT));
                            _supportsDirect3D9Ex = true;
                        }
                    }
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
                new Direct3D9Device_DrawIndexedPrimitiveDelegate(DrawIndexedPrimitive),
                this);

            Direct3DDevice_SetStreamSourceHook = new Hook<Direct3D9Device_SetStreamSourceDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.SetStreamSource],
                new Direct3D9Device_SetStreamSourceDelegate(NewSetStreamSource),
                this);


            Direct3DDevice_SetTextureHook = new Hook<Direct3D9Device_SetTextureDelegate>(
                id3dDeviceFunctionAddresses[(int)Direct3DDevice9FunctionOrdinals.SetTexture],
                new Direct3D9Device_SetTextureDelegate(NewSetTexture),
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

        /// <summary>
        /// The IDirect3DDevice9.EndScene function definition
        /// </summary>
        /// <param name="device"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_EndSceneDelegate(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        unsafe delegate int Direct3D9Device_CreateQueryDelegate(IntPtr devicePtr, int Type1, IntPtr ppQuery);



        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_DrawIndexedPrimitiveDelegate(IntPtr devicePtr, SharpDX.Direct3D9.PrimitiveType arg0, int baseVertexIndex, int minVertexIndex, int numVertices, int startIndex, int primCount);


        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_SetStreamSourceDelegate(IntPtr devicePtr, uint StreamNumber, IntPtr pStreamData, uint OffsetInBytes, uint sStride);



        int NewSetStreamSource(IntPtr devicePtr, uint StreamNumber, IntPtr pStreamData, uint OffsetInBytes, uint sStride)
        {
            if (StreamNumber == 0)
            {
                Stride = sStride;
            }
            Direct3DDevice_SetStreamSourceHook.Original(devicePtr, StreamNumber, pStreamData, OffsetInBytes, sStride);
            return 1;
        }


        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_SetTextureDelegate(IntPtr devicePtr, uint Sampler, IntPtr pTexture);


        uint Stride;
        uint NumVertices;
        uint PrimCount;
        int vSize;
        struct WeaponEspInfo
        {
            public float pOutX, pOutY, RealDistance;
            //float CrosshairDistance;
            //string oName;
        };
        int NewSetTexture(IntPtr devicePtr, uint Sampler, IntPtr pTexture)
        {
            device = (Device)devicePtr;
            try
            {
                var vShader = device.VertexShader;
                if (vShader != null)
                {
                    if (vShader.Function.BufferSize != null)
                    {
                        vSize = vShader.Function.BufferSize;
                    }
                }
                if (vShader != null)
                {
                    vShader.Dispose();
                }
                var primCount = PrimCount;

                if (((Stride == 72 && vSize == 1836) || (Stride == 72 && NumVertices == 194 && primCount == 352) || (Stride == 72 && NumVertices == 1729 && primCount == 2960) || (Stride == 72 && NumVertices == 1362 && primCount == 2206) || (Stride == 72 && NumVertices == 1605 && primCount == 2872) || (Stride == 72 && NumVertices == 1198 && primCount == 2172) || (Stride == 72 && NumVertices == 406 && primCount == 632) || (Stride == 72 && NumVertices == 529 && primCount == 878) || (Stride == 72 && NumVertices == 696 && primCount == 1082) || (Stride == 32 && NumVertices == 645 && primCount == 1062) || (Stride == 32 && NumVertices == 493 && primCount == 826) || (Stride == 72 && NumVertices == 645 && primCount == 1062) || (Stride == 72 && NumVertices == 522 && primCount == 838) || (Stride == 72 && NumVertices == 2140 && primCount == 3736) || (Stride == 72 && NumVertices == 1626 && primCount == 2716) || (Stride == 24 && NumVertices == 493 && primCount == 806) || (Stride == 56 && NumVertices == 1055 && primCount == 1234) || (Stride == 56 && NumVertices == 926 && primCount == 1516) || (Stride == 56 && NumVertices == 1607 && primCount == 1916) || (Stride == 72 && NumVertices == 1184 && primCount == 1832) || (Stride == 72 && NumVertices == 1532 && primCount == 2580) || (Stride == 72 && NumVertices == 237 && primCount == 3806) || (Stride == 72 && NumVertices == 2913 && primCount == 4704) || (Stride == 72 && NumVertices == 3046 && primCount == 5422) || (Stride == 72 && NumVertices == 2906 && primCount == 4634) || (Stride == 72 && NumVertices == 1529 && primCount == 2734) || (Stride == 72 && NumVertices == 3672 && primCount == 6604) || (Stride == 72 && NumVertices == 4004 && primCount == 5326) || (Stride == 32 && NumVertices == 972 && primCount == 1696) || (Stride == 32 && NumVertices == 1998 && primCount == 3092) || (Stride == 72 && NumVertices == 1030 && primCount == 1768) || (Stride == 32 && NumVertices == 1844 && primCount == 2980) || (Stride == 72 && NumVertices == 1182 && primCount == 1940) || (Stride == 72 && NumVertices == 2237 && primCount == 3806) || (Stride == 72 && NumVertices == 253 && primCount == 358) || (Stride == 72 && NumVertices == 1224 && primCount == 2086) || (Stride == 72 && NumVertices == 124 && primCount == 164) || (Stride == 72 && NumVertices == 705 && primCount == 1188) || (Stride == 72 && NumVertices == 1411 && primCount == 1160) || (Stride == 72 && NumVertices == 1750 && primCount == 1440) || (Stride == 32 && NumVertices == 1411 && primCount == 1160) || (Stride == 32 && NumVertices == 1750 && primCount == 1440) || (Stride == 72 && NumVertices == 1477 && primCount == 1216) || (Stride == 72 && NumVertices == 1414 && primCount == 1754) || (Stride == 72 && NumVertices == 90 && primCount == 112) || (Stride == 56 && NumVertices == 3506 && primCount == 2167) || (Stride == 72 && NumVertices == 2544 && primCount == 3800) || (Stride == 72 && NumVertices == 2785 && primCount == 4136) || (Stride == 56 && NumVertices == 1678 && primCount == 1759) || (Stride == 72 && NumVertices == 5082 && primCount == 5086) || (Stride == 56 && NumVertices == 3068 && primCount == 1789) || (Stride == 72 && NumVertices == 6774 && primCount == 6882) || (Stride == 72 && NumVertices == 2215 && primCount == 3818) || (Stride == 72 && NumVertices == 1337 && primCount == 2376) || (Stride == 72 && NumVertices == 2292 && primCount == 3482) || (Stride == 72 && NumVertices == 3258 && primCount == 3931) || (Stride == 32 && NumVertices == 3643 && primCount == 3216) || (Stride == 72 && NumVertices == 2442 && primCount == 4632) || (Stride == 72 && NumVertices == 3585 && primCount == 3914) || (Stride == 72 && NumVertices == 3776 && primCount == 3416) || (Stride == 72 && NumVertices == 3563 && primCount == 3130) || (Stride == 72 && NumVertices == 3279 && primCount == 2945) || (Stride == 72 && NumVertices == 4478 && primCount == 4127) || (Stride == 72 && NumVertices == 1682 && primCount == 2866) || (Stride == 72 && NumVertices == 144 && primCount == 216) || (Stride == 72 && NumVertices == 689 && primCount == 1156) || (Stride == 72 && NumVertices == 58 && primCount == 56) || (Stride == 72 && NumVertices == 1692 && primCount == 2884) || (Stride == 72 && NumVertices == 1354 && primCount == 2202) || (Stride == 72 && NumVertices == 1705 && primCount == 3076) || (Stride == 80 && NumVertices == 614 && primCount == 828) || (Stride == 72 && NumVertices == 1222 && primCount == 2214) || (Stride == 72 && NumVertices == 356 && primCount == 534) || (Stride == 72 && NumVertices == 112 && primCount == 152) || (Stride == 72 && NumVertices == 21 && primCount == 24) || (Stride == 72 && NumVertices == 1194 && primCount == 2066)))
                {
                    //AddWeapons(devicePtr);
                    //设置墙后颜色
                    //device.SetTexture(0, textureBack);
                }
            }
            catch(Exception ex)
            {
                this.DebugMessage(ex.Message);
            }
            Direct3DDevice_SetTextureHook.Original(devicePtr, Sampler, pTexture);
            return 1;
        }
        List<WeaponEspInfo> WeaponEspInfoList = new List<WeaponEspInfo>();
        SharpDX.Mathematics.Interop.RawViewport Viewport;
        void AddWeapons(IntPtr devicePtr)
        {
            try
            {
                device = (Device)devicePtr;
                var floatC = device.GetVertexShaderFloatConstant(0, 4);
                var floatN = new float[16];
                floatN[3] = floatC[0];
                floatN[7] = floatC[1];
                floatN[11] = floatC[2];
                floatN[15] = floatC[3];
                var matrix = new SharpDX.Matrix(floatN);
                SharpDX.Vector3 pOut;
                SharpDX.Vector3 pIn = new SharpDX.Vector3(0, 3, 0);
                float distance = pIn.X * matrix.M14 + pIn.Y * matrix.M24 + pIn.Z * matrix.M34 + matrix.M44;
                matrix = SharpDX.Matrix.Transpose(matrix);
                pOut = SharpDX.Vector3.TransformCoordinate(pIn, matrix);
                //var Viewport = device.Viewport;
               
                pOut.X = Viewport.X + (1.0f + pOut.X) * Viewport.Width / 2.0f;
                pOut.Y = Viewport.Y + (1.0f - pOut.Y) * Viewport.Height / 2.0f;
                //float x1, y1;
                //if (pOut.X > 0.0f && pOut.Y > 0.0f && pOut.X < Viewport.Width && pOut.Y < Viewport.Height)
                //{
                //    x1 = pOut.X;
                //    y1 = pOut.Y;
                    
                //    //var line1 = new Line(device);
                //    //List<SharpDX.Mathematics.Interop.RawVector2> rawVector2s1 = new List<SharpDX.Mathematics.Interop.RawVector2>();
                //    //line1.Width = 1;
                //    //rawVector2s1.Add(new SharpDX.Mathematics.Interop.RawVector2(pOut.X, pOut.Y + 5));
                //    //rawVector2s1.Add(new SharpDX.Mathematics.Interop.RawVector2(pOut.X, pOut.Y - 5));
                //    //rawVector2s1.Add(new SharpDX.Mathematics.Interop.RawVector2(pOut.X + 5, pOut.Y));
                //    //rawVector2s1.Add(new SharpDX.Mathematics.Interop.RawVector2(pOut.X - 5, pOut.Y));
                //    //line1.Draw(rawVector2s.ToArray(), new SharpDX.Mathematics.Interop.RawColorBGRA(0, 255, 0, 40));
                //}
                //else
                //{
                //    x1 = -1.0f;
                //    y1 = -1.0f;
                //}

                

                ////this.DebugMessage(pOut.X + "," + pOut.Y + "," + distance);
                //WeaponEspInfo pWeaponEspInfo = new WeaponEspInfo { pOutX = x1, pOutY = y1, RealDistance = distance };
                //WeaponEspInfoList.Add(pWeaponEspInfo);
            }
            catch(Exception ex )
            {
                this.DebugMessage(ex.Message);
            }
        }


        /// <summary>
        /// The IDirect3DDevice9.Reset function definition
        /// </summary>
        /// <param name="device"></param>
        /// <param name="presentParameters"></param>
        /// <returns></returns>
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        delegate int Direct3D9Device_ResetDelegate(IntPtr device, ref PresentParameters presentParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        unsafe delegate int Direct3D9Device_PresentDelegate(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        unsafe delegate int Direct3D9DeviceEx_PresentExDelegate(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion, Present dwFlags);

        private ICollection<string> ignoreds = new List<string>();
        static Device device;
        static bool isFirstDrawIndexed = true;
        List<string> hh = "121968,86256,38808,288288,6480,110088,95616,349560,204336,377424,195120,357912,218880,95472,98064,177120,100224,86688,78768,411768,140112,384624,691200,10800,11808,18432,258120,129456,140616,200160,146592,294480,84816,134784,124488,48816,17640,97488,18000,239616,84528,77904,189648,29016,121248,12816,39528,20016,179856,144648,199440,48312,123552,124560,13968".Split(',').ToList();
        int DrawIndexedPrimitive(IntPtr devicePtr, SharpDX.Direct3D9.PrimitiveType arg0, int baseVertexIndex, int minVertexIndex, int numVertices, int startIndex, int primCount)
        {
            NumVertices = (uint)numVertices;
            PrimCount = (uint)primCount;
            Direct3DDevice_DrawIndexedPrimitiveHook.Original(devicePtr, arg0, baseVertexIndex, minVertexIndex, numVertices, startIndex, primCount);
            return 1;
        }

        int CreateQueryHook(IntPtr devicePtr, int Type, IntPtr ppQuery)
        {
            //QueryType.
            //this.DebugMessage(Type1.ToString()+":"+ devicePtr.ToString());
            if (Type == 9)
            {
                Type = 10;
            }
            Direct3DDevice_CreateQueryHook.Original(devicePtr, 11, ppQuery);
            return 1;
        }

        static string[] list = ("16928#32").Split(',');
        //static string[] list = ("85104#72,74160#72,59008#32,63936#32,121968#72,179856#72,288288#72,6480#72,110088#72,264384#72,12816#72,144648#72,10800#72,95616#72,27104#56,22224#24,59080#56,66416#56,29808#24,61712#56,100576#56,101024#56,154080#72,117072#72,123552#72,134784#72,16928#32,100224#72,177120#72,209232#72,199440#72,357912#72,218880#72,377424#72,39528#72,195120#72,48312#72,18216#72,161064#72,8928#72,209736#72,5400#72,162792#72,50760#72,110304#72").Split(',');
        /// <summary>
        /// Reset the _renderTarget so that we are sure it will have the correct presentation parameters (required to support working across changes to windowed/fullscreen or resolution changes)
        /// </summary>
        /// <param name="devicePtr"></param>
        /// <param name="presentParameters"></param>
        /// <returns></returns>
        int ResetHook(IntPtr devicePtr, ref PresentParameters presentParameters)
        {
            // Ensure certain overlay resources have performed necessary pre-reset tasks
            if (_overlayEngine != null)
                _overlayEngine.BeforeDeviceReset();

            Cleanup();

            return Direct3DDevice_ResetHook.Original(devicePtr, ref presentParameters);
        }

        bool _isUsingPresent = false;

        // Used in the overlay
        unsafe int PresentExHook(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion, Present dwFlags)
        {
            //PresentHook(devicePtr);
            _isUsingPresent = true;
            DeviceEx device = (DeviceEx)devicePtr;



            DoCaptureRenderTarget(device, "PresentEx");
            setColor(devicePtr, 0);
            return Direct3DDeviceEx_PresentExHook.Original(devicePtr, pSourceRect, pDestRect, hDestWindowOverride, pDirtyRegion, dwFlags);
        }
        void setColor(IntPtr devicePtr, int isEx)
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
                        Viewport = ((DeviceEx)devicePtr).Viewport;
                        
                    }
                    else
                    {
                        textureBack = Texture.FromFile((Device)devicePtr, fileNameBack);
                        textureFront = Texture.FromFile((Device)devicePtr, fileNameFront);
                        Viewport = ((Device)devicePtr).Viewport;
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


            if (isLine)
            {
                line = new Line(device);
                isLine = false;
                var view = device.Viewport;
                int Vx = view.Width / 2;
                int Vy = view.Height / 2;
                for (int i = 0; i < 20; i++)
                {
                    rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx, Vy + i));
                    rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx, Vy - i));
                    rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx + i, Vy));
                    rawVector2s.Add(new SharpDX.Mathematics.Interop.RawVector2(Vx - i, Vy));
                }
                line.Width = 1;
            }

        }


        unsafe int PresentHook(IntPtr devicePtr, SharpDX.Rectangle* pSourceRect, SharpDX.Rectangle* pDestRect, IntPtr hDestWindowOverride, IntPtr pDirtyRegion)
        {
            PresentHook(devicePtr);

            return Direct3DDevice_PresentHook.Original(devicePtr, pSourceRect, pDestRect, hDestWindowOverride, pDirtyRegion);
        }

        private unsafe void PresentHook(IntPtr devicePtr)
        {
            _isUsingPresent = true;

            Device device = (Device)devicePtr;



            DoCaptureRenderTarget(device, "PresentHook");
            setColor(devicePtr, 1);

        }

        Line line;
        List<SharpDX.Mathematics.Interop.RawVector2> rawVector2s = new List<SharpDX.Mathematics.Interop.RawVector2>();
        bool isLine = true;


        /// <summary>
        /// Hook for IDirect3DDevice9.EndScene
        /// </summary>
        /// <param name="devicePtr">Pointer to the IDirect3DDevice9 instance. Note: object member functions always pass "this" as the first parameter.</param>
        /// <returns>The HRESULT of the original EndScene</returns>
        /// <remarks>Remember that this is called many times a second by the Direct3D application - be mindful of memory and performance!</remarks>
        int EndSceneHook(IntPtr devicePtr)
        {
            //this.DebugMessage("Hook: ceshi");
            Device device1 = (Device)devicePtr;
            if (!_isUsingPresent)
                DoCaptureRenderTarget(device1, "EndSceneHook");
            //if(line!=null)
            //{
            //    line.Draw(rawVector2s.ToArray(), new SharpDX.Mathematics.Interop.RawColorBGRA(0, 255, 0, 15));
            //}

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
            //WeaponEspInfoList.Clear();
            return Direct3DDevice_EndSceneHook.Original(devicePtr);
        }

        Capture.Hook.DX9.DXOverlayEngine _overlayEngine;

        /// <summary>
        /// Implementation of capturing from the render target of the Direct3D9 Device (or DeviceEx)
        /// </summary>
        /// <param name="device"></param>
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
