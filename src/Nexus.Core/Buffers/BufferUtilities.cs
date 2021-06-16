using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Linq;
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

            var method = typeof(BufferUtilities)
                .GetMethod(nameof(BufferUtilities.InternalApplyDatasetStatusByDataType), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(targetType);

            var doubleData = (double[])method
                .Invoke(null, new object[] { result });

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

        private static double[] InternalApplyDatasetStatusByDataType<T>(ReadResult result)
            where T : unmanaged
        {
            return BufferUtilities.ApplyDatasetStatus(result.GetData<T>().Span, result.Status.Span);
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
