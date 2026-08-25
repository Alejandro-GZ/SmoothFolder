using System.Runtime.InteropServices;
using Windows.Graphics.Effects;
using WinRT;
using WinRT.Interop;

namespace Windows.Graphics.Effects.Interop
{
    public enum GraphicsEffectPropertyMapping
    {
        Unknown = 0,
        Direct = 1,
        VectorX = 2,
        VectorY = 3,
        VectorZ = 4,
        VectorW = 5,
        RectToVector4 = 6,
        RadiansToDegrees = 7,
        ColorMatrixAlphaMode = 8,
        ColorToVector3 = 9,
        ColorToVector4 = 10
    }

    /// <summary>
    /// Managed projection of the native-only IGraphicsEffectD2D1Interop
    /// contract from windows.graphics.effects.interop.h.
    ///
    /// CsWinRT custom interop interfaces must expose their ABI helper as a
    /// nested Vftbl on the projected interface itself. This is the same shape
    /// used by the CsWinRT custom-interface examples.
    /// </summary>
    [WindowsRuntimeType]
    [WindowsRuntimeHelperType(typeof(IGraphicsEffectD2D1Interop))]
    [Guid("2FC57384-A068-44D7-A331-30982FCF7177")]
    public interface IGraphicsEffectD2D1Interop
    {
        Guid EffectId { get; }

        uint GetNamedPropertyMapping(
            string name,
            out GraphicsEffectPropertyMapping mapping);

        object GetProperty(uint index);

        uint PropertyCount { get; }

        IGraphicsEffectSource GetSource(uint index);

        uint SourceCount { get; }

        [Guid("2FC57384-A068-44D7-A331-30982FCF7177")]
        public struct Vftbl
        {
            // IID_Windows_Foundation_IPropertyValue. The public Windows SDK
            // projection keeps IPropertyValue internal, so QueryInterface is
            // performed against its native IID at the ABI boundary.
            private static readonly Guid IPropertyValueId =
                new("4BD682DD-7554-40E9-9A9B-82654EDE7E62");

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetEffectIdDelegate(
                IntPtr thisPtr,
                out Guid id);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetNamedPropertyMappingDelegate(
                IntPtr thisPtr,
                IntPtr name,
                out uint index,
                out GraphicsEffectPropertyMapping mapping);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetPropertyCountDelegate(
                IntPtr thisPtr,
                out uint count);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetPropertyDelegate(
                IntPtr thisPtr,
                uint index,
                out IntPtr value);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetSourceDelegate(
                IntPtr thisPtr,
                uint index,
                out IntPtr source);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            public delegate int GetSourceCountDelegate(
                IntPtr thisPtr,
                out uint count);

            public IUnknownVftbl IUnknownVftbl;
            public GetEffectIdDelegate GetEffectId;
            public GetNamedPropertyMappingDelegate GetNamedPropertyMapping;

            // Native windows.graphics.effects.interop.h vtable order:
            // GetPropertyCount, GetProperty, GetSource, GetSourceCount.
            // Keep this exact order: the C++ header declaration is the ABI
            // authority; documentation method lists are not vtable layouts.
            public GetPropertyCountDelegate GetPropertyCount;
            public GetPropertyDelegate GetProperty;
            public GetSourceDelegate GetSource;
            public GetSourceCountDelegate GetSourceCount;

            public static readonly Vftbl AbiToProjectionVftable;
            public static readonly IntPtr AbiToProjectionVftablePtr;

            static Vftbl()
            {
                AbiToProjectionVftable =
                    new Vftbl
                    {
                        IUnknownVftbl =
                            IUnknownVftbl.AbiToProjectionVftbl,
                        GetEffectId =
                            DoGetEffectId,
                        GetNamedPropertyMapping =
                            DoGetNamedPropertyMapping,
                        GetPropertyCount =
                            DoGetPropertyCount,
                        GetProperty =
                            DoGetProperty,
                        GetSource =
                            DoGetSource,
                        GetSourceCount =
                            DoGetSourceCount
                    };

                AbiToProjectionVftablePtr =
                    Marshal.AllocHGlobal(
                        Marshal.SizeOf<Vftbl>());

                Marshal.StructureToPtr(
                    AbiToProjectionVftable,
                    AbiToProjectionVftablePtr,
                    false);
            }

            private static int DoGetEffectId(
                IntPtr thisPtr,
                out Guid id)
            {
                id = default;

                try
                {
                    id =
                        FindManagedObject(
                            thisPtr).EffectId;

                    return 0;
                }
                catch (Exception ex)
                {
                    ExceptionHelpers.SetErrorInfo(ex);
                    return Marshal.GetHRForException(ex);
                }
            }

            private static int DoGetNamedPropertyMapping(
                IntPtr thisPtr,
                IntPtr name,
                out uint index,
                out GraphicsEffectPropertyMapping mapping)
            {
                index = uint.MaxValue;
                mapping =
                    GraphicsEffectPropertyMapping.Unknown;

                try
                {
                    var managedName =
                        Marshal.PtrToStringUni(name)
                        ?? string.Empty;

                    index =
                        FindManagedObject(
                            thisPtr)
                        .GetNamedPropertyMapping(
                            managedName,
                            out mapping);

                    return 0;
                }
                catch (Exception ex)
                {
                    ExceptionHelpers.SetErrorInfo(ex);
                    return Marshal.GetHRForException(ex);
                }
            }

            private static int DoGetPropertyCount(
                IntPtr thisPtr,
                out uint count)
            {
                count = 0;

                try
                {
                    count =
                        FindManagedObject(
                            thisPtr).PropertyCount;

                    return 0;
                }
                catch (Exception ex)
                {
                    ExceptionHelpers.SetErrorInfo(ex);
                    return Marshal.GetHRForException(ex);
                }
            }

            private static int DoGetProperty(
                IntPtr thisPtr,
                uint index,
                out IntPtr value)
            {
                value = IntPtr.Zero;

                try
                {
                    var property =
                        FindManagedObject(
                            thisPtr).GetProperty(
                            index);

                    var inspectable =
                        MarshalInspectable<object>.FromManaged(
                            property);

                    if (inspectable == IntPtr.Zero)
                    {
                        throw new InvalidOperationException(
                            "Could not marshal the graphics-effect property.");
                    }

                    try
                    {
                        var iid =
                            IPropertyValueId;

                        var queryResult =
                            Marshal.QueryInterface(
                                inspectable,
                                ref iid,
                                out value);

                        if (queryResult < 0)
                            Marshal.ThrowExceptionForHR(queryResult);

                        if (value == IntPtr.Zero)
                        {
                            throw new InvalidOperationException(
                                "The graphics-effect property does not expose IPropertyValue.");
                        }
                    }
                    finally
                    {
                        MarshalInspectable<object>.DisposeAbi(
                            inspectable);
                    }

                    return 0;
                }
                catch (Exception ex)
                {
                    if (value != IntPtr.Zero)
                    {
                        _ = Marshal.Release(value);
                        value = IntPtr.Zero;
                    }

                    ExceptionHelpers.SetErrorInfo(ex);
                    return Marshal.GetHRForException(ex);
                }
            }

            private static int DoGetSource(
                IntPtr thisPtr,
                uint index,
                out IntPtr source)
            {
                source = IntPtr.Zero;

                try
                {
                    var managedSource =
                        FindManagedObject(
                            thisPtr).GetSource(
                            index);

                    source =
                        MarshalInterface<IGraphicsEffectSource>.FromManaged(
                            managedSource);

                    return 0;
                }
                catch (Exception ex)
                {
                    ExceptionHelpers.SetErrorInfo(ex);
                    return Marshal.GetHRForException(ex);
                }
            }

            private static int DoGetSourceCount(
                IntPtr thisPtr,
                out uint count)
            {
                count = 0;

                try
                {
                    count =
                        FindManagedObject(
                            thisPtr).SourceCount;

                    return 0;
                }
                catch (Exception ex)
                {
                    ExceptionHelpers.SetErrorInfo(ex);
                    return Marshal.GetHRForException(ex);
                }
            }

            private static IGraphicsEffectD2D1Interop FindManagedObject(
                IntPtr thisPtr) =>
                ComWrappersSupport.FindObject<IGraphicsEffectD2D1Interop>(
                    thisPtr);
        }
    }
}
