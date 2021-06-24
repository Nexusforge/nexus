using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Nexus
{
    public static class BufferUtilities
    {
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

        public unsafe static double[] ApplyDatasetStatus<T>(Memory<T> data, Memory<byte> status) where T : unmanaged
        {
            var doubleData = new double[data.Length];

            fixed (T* dataPtr = data.Span)
            {
                fixed (byte* statusPtr = status.Span)
                {
                    BufferUtilities.InternalApplyDatasetStatus(dataPtr, statusPtr, doubleData);
                }
            }

            return doubleData;
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
            return BufferUtilities.ApplyDatasetStatus(result.GetData<T>(), result.Status);
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
