using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Nexus.Buffers
{
    public static class BufferUtilities
    {
        private static object targetType;

        public static unsafe double[] ToDouble<T>(Span<T> dataset) where T : unmanaged
        {
            var doubleData = new double[dataset.Length];

            fixed (T* dataPtr = dataset)
            {
                BufferUtilities.InternalToDouble(dataPtr, doubleData);
            }

            return doubleData;
        }

        public static double[] ApplyDatasetStatusByDataType(NexusDataType dataType, ReadResult result)
        {
            var targetType = NexusUtilities.GetTypeFromNexusDataType(dataType);

            // Invoke xxx
            var method = typeof(MemoryMarshal).GetMethod(nameof(MemoryMarshal.Cast), BindingFlags.Public | BindingFlags.Static);
            method = method.MakeGenericMethod(typeof(byte), targetType);
            var castedData = method.Invoke(null, new object[] { result.Data });

            // Invoke ApplyDatasetStatus
            var method2 = typeof(BufferUtilities).GetMethod(nameof(BufferUtilities.ApplyDatasetStatus), BindingFlags.Public | BindingFlags.Static);
            var doubleData = (double[])method2.Invoke(null, new object[] { castedData, result.Status });

            return doubleData;
        }

        public unsafe static double[] ApplyDatasetStatus<T>(Span<T> data, Span<byte> status) where T : unmanaged
        {
            var doubleData = new double[data.Length];

            fixed (T* dataPtr = data)
            {
                fixed (byte* statusPtr = status)
                {
                    BufferUtilities.InternalApplyDatasetStatus(dataPtr, statusPtr, doubleData);
                }
            }

            return doubleData;
        }

        public static ISimpleBuffer CreateSimpleBuffer(double[] data)
        {
            return new SimpleBuffer(data);
        }

        public static IExtendedBuffer CreateExtendedBuffer(NexusDataType dataType, int length)
        {
            var type = typeof(ExtendedBuffer<>).MakeGenericType(new Type[] { NexusUtilities.GetTypeFromNexusDataType(dataType) });
            return (IExtendedBuffer)Activator.CreateInstance(type, length);
        }

        internal static unsafe void InternalToDouble<T>(T* dataPtr, double[] doubleData) where T : unmanaged
        {
            Parallel.For(0, doubleData.Length, i =>
            {
                doubleData[i] = GenericToDouble<T>.ToDouble(dataPtr[i]);
            });
        }

        private unsafe static void InternalApplyDatasetStatus<T>(T* dataPtr, byte* statusPtr, double[] doubleData) where T : unmanaged
        {
            Parallel.For(0, doubleData.Length, i =>
            {
                if (statusPtr[i] != 1)
                    doubleData[i] = double.NaN;
                else
                    doubleData[i] = GenericToDouble<T>.ToDouble(dataPtr[i]);
            });
        }
    }
}
