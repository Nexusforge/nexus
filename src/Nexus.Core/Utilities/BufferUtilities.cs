using Nexus.Extensibility;
using Nexus.Infrastructure;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Nexus.Utilities
{
    public static class BufferUtilities
    {
        public static void ApplyDatasetStatusByDataType(NexusDataType dataType, ReadResult result, Memory<double> target)
        {
            var targetType = NexusCoreUtilities.GetTypeFromNexusDataType(dataType);

            var method = typeof(BufferUtilities)
                .GetMethod(nameof(BufferUtilities.InternalApplyDatasetStatusByDataType), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(targetType);

            method.Invoke(null, new object[] { result, target });
        }

        private static void InternalApplyDatasetStatusByDataType<T>(ReadResult result, Memory<double> target)
            where T : unmanaged
        {
            BufferUtilities.ApplyDatasetStatus<T>(result.GetData<T>(), result.Status, target);
        }

        public unsafe static void ApplyDatasetStatus<T>(ReadOnlyMemory<T> data, ReadOnlyMemory<byte> status, Memory<double> target) where T : unmanaged
        {
            fixed (T* dataPtr = data.Span)
            {
                fixed (byte* statusPtr = status.Span)
                {
                    fixed (double* targetPtr = target.Span)
                    {
                        BufferUtilities.InternalApplyDatasetStatus(data.Length, dataPtr, statusPtr, targetPtr);
                    }
                }
            }
        }

        private unsafe static void InternalApplyDatasetStatus<T>(int length, T* dataPtr, byte* statusPtr, double* targetPtr) where T : unmanaged
        {
            Parallel.For(0, length, i =>
            {
                if (statusPtr[i] != 1)
                    targetPtr[i] = double.NaN;

                else
                    targetPtr[i] = GenericToDouble<T>.ToDouble(dataPtr[i]);
            });
        }
    }
}
