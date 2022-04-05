﻿using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
using Microsoft.Extensions.Options;
using Nexus.Core;
using Nexus.DataModel;
using Nexus.Utilities;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Nexus.Services
{
    internal interface IProcessingService
    {
        void Aggregate(
            NexusDataType dataType,
            RepresentationKind kind,
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> status,
            Memory<double> targetBuffer,
            int blockSize);
    }

    internal class ProcessingService : IProcessingService
    {
        private double _nanThreshold;

        public ProcessingService(IOptions<DataOptions> dataOptions)
        {
            _nanThreshold = dataOptions.Value.AggregationNaNThreshold;
        }

        public void Aggregate(
            NexusDataType dataType,
            RepresentationKind kind,
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> status,
            Memory<double> targetBuffer,
            int blockSize)
        {
            var targetType = NexusCoreUtilities.GetTypeFromNexusDataType(dataType);

            var method = typeof(ProcessingService)
                .GetMethod(nameof(ProcessingService.GenericAggregate), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(targetType);

            method.Invoke(this, new object[] { kind, data, status, targetBuffer, blockSize});
        }

        private void GenericAggregate<T>(
            RepresentationKind kind,
            ReadOnlyMemory<byte> data,
            ReadOnlyMemory<byte> status,
            Memory<double> targetBuffer,
            int blockSize) where T : unmanaged
        {
            var Tdata = new ReadonlyCastMemoryManager<byte, T>(data).Memory;

            switch (kind)
            {
                case RepresentationKind.Mean:
                case RepresentationKind.MeanPolarDeg:
                case RepresentationKind.Min:
                case RepresentationKind.Max:
                case RepresentationKind.Std:
                case RepresentationKind.Rms:
                case RepresentationKind.Sum:

                    var doubleData = new double[Tdata.Length];

                    BufferUtilities.ApplyRepresentationStatus<T>(Tdata, status, target: doubleData);
                    ApplyAggregationFunction(kind, blockSize, doubleData, targetBuffer);

                    break;

                case RepresentationKind.MinBitwise:
                case RepresentationKind.MaxBitwise:

                    ApplyAggregationFunction<T>(kind, blockSize, Tdata, status, targetBuffer);

                    break;

                default:
                    throw new Exception($"The representation kind {kind} is not supported.");
            }
        }

        private void ApplyAggregationFunction(
            RepresentationKind kind,
            int blockSize,
            ReadOnlyMemory<double> data,
            Memory<double> targetBuffer)
        {
            switch (kind)
            {
                case RepresentationKind.Mean:

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data, x, blockSize);
                        var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                            targetBuffer.Span[x] = ArrayStatistics.Mean(chunkData);

                        else
                            targetBuffer.Span[x] = double.NaN;
                    });

                    break;

                case RepresentationKind.MeanPolarDeg:

                    var sin = new double[targetBuffer.Length];
                    var cos = new double[targetBuffer.Length];
                    var limit = 360;
                    var factor = 2 * Math.PI / limit;

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data, x, blockSize);
                        var length = chunkData.Length;
                        var isHighQuality = (length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                        {
                            for (int i = 0; i < chunkData.Length; i++)
                            {
                                sin[x] += Math.Sin(chunkData[i] * factor);
                                cos[x] += Math.Cos(chunkData[i] * factor);
                            }

                            targetBuffer.Span[x] = Math.Atan2(sin[x], cos[x]) / factor;

                            if (targetBuffer.Span[x] < 0)
                                targetBuffer.Span[x] += limit;
                        }
                        else
                        {
                            targetBuffer.Span[x] = double.NaN;
                        }
                    });

                    break;

                case RepresentationKind.Min:

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data, x, blockSize);
                        var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                            targetBuffer.Span[x] = ArrayStatistics.Minimum(chunkData);

                        else
                            targetBuffer.Span[x] = double.NaN;
                    });

                    break;

                case RepresentationKind.Max:

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data, x, blockSize);
                        var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                            targetBuffer.Span[x] = ArrayStatistics.Maximum(chunkData);

                        else
                            targetBuffer.Span[x] = double.NaN;
                    });

                    break;

                case RepresentationKind.Std:

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data, x, blockSize);
                        var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                            targetBuffer.Span[x] = ArrayStatistics.StandardDeviation(chunkData);

                        else
                            targetBuffer.Span[x] = double.NaN;
                    });

                    break;

                case RepresentationKind.Rms:

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data, x, blockSize);
                        var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                            targetBuffer.Span[x] = ArrayStatistics.RootMeanSquare(chunkData);

                        else
                            targetBuffer.Span[x] = double.NaN;
                    });

                    break;

                case RepresentationKind.Sum:

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data, x, blockSize);
                        var isHighQuality = (chunkData.Length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                            targetBuffer.Span[x] = Vector<double>.Build.Dense(chunkData).Sum();

                        else
                            targetBuffer.Span[x] = double.NaN;
                    });

                    break;

                default:
                    throw new Exception($"The representation kind {kind} is not supported.");

            }
        }

        private void ApplyAggregationFunction<T>(
            RepresentationKind kind,
            int blockSize,
            ReadOnlyMemory<T> data,
            ReadOnlyMemory<byte> status,
            Memory<double> targetBuffer) where T : unmanaged
        {
            switch (kind)
            {
                case RepresentationKind.MinBitwise:

                    T[] bitField_and = new T[targetBuffer.Length];

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data.Span, status.Span, x, blockSize);
                        var length = chunkData.Length;
                        var isHighQuality = (length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                        {
                            for (int i = 0; i < length; i++)
                            {
                                if (i == 0)
                                    bitField_and[x] = GenericBitOr<T>.BitOr(bitField_and[x], chunkData[i]);
                                else
                                    bitField_and[x] = GenericBitAnd<T>.BitAnd(bitField_and[x], chunkData[i]);
                            }

                            targetBuffer.Span[x] = Convert.ToDouble(bitField_and[x]);
                        }
                        else
                        {
                            targetBuffer.Span[x] = double.NaN;
                        }
                    });

                    break;

                case RepresentationKind.MaxBitwise:

                    T[] bitField_or = new T[targetBuffer.Length];

                    Parallel.For(0, targetBuffer.Length, x =>
                    {
                        var chunkData = GetNaNFreeData(data.Span, status.Span, x, blockSize);
                        var length = chunkData.Length;
                        var isHighQuality = (length / (double)blockSize) >= _nanThreshold;

                        if (isHighQuality)
                        {
                            for (int i = 0; i < length; i++)
                            {
                                bitField_or[x] = GenericBitOr<T>.BitOr(bitField_or[x], chunkData[i]);
                            }

                            targetBuffer.Span[x] = Convert.ToDouble(bitField_or[x]);
                        }
                        else
                        {
                            targetBuffer.Span[x] = double.NaN;
                        }
                    });

                    break;

                default:
                    throw new Exception($"The representation kind {kind} is not supported.");

            }
        }

        private unsafe T[] GetNaNFreeData<T>(ReadOnlySpan<T> data, ReadOnlySpan<byte> status, int index, int kernelSize) where T : unmanaged
        {
            var sourceIndex = index * kernelSize;
            var length = data.Length;
            var chunkData = new List<T>();

            for (int i = 0; i < length; i++)
            {
                if (status[sourceIndex + i] == 1)
                    chunkData.Add(data[sourceIndex + i]);
            }

            return chunkData.ToArray();
        }

        private double[] GetNaNFreeData(ReadOnlyMemory<double> data, int index, int kernelSize)
        {
            var sourceIndex = index * kernelSize;

            return MemoryMarshal.ToEnumerable<double>(data)
                .Skip(sourceIndex)
                .Take(kernelSize)
                .Where(value => !double.IsNaN(value))
                .ToArray();
        }
    }
}
