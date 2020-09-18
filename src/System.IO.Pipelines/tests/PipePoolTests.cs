﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Buffers;
using System.Threading.Tasks;
using Xunit;

namespace System.IO.Pipelines.Tests
{
    public class PipePoolTests
    {
        [Fact]
        public async Task AdvanceToEndReturnsAllBlocks()
        {
            var pool = new DisposeTrackingBufferPool();

            var writeSize = 512;

            var pipe = new Pipe(new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));
            while (pool.CurrentlyRentedBlocks != 3)
            {
                PipeWriter writableBuffer = pipe.Writer.WriteEmpty(writeSize);
                await writableBuffer.FlushAsync();
            }

            ReadResult readResult = await pipe.Reader.ReadAsync();
            pipe.Reader.AdvanceTo(readResult.Buffer.End);

            Assert.Equal(0, pool.CurrentlyRentedBlocks);
            Assert.Equal(3, pool.DisposedBlocks);
        }

        [Fact]
        public async Task CanWriteAfterReturningMultipleBlocks()
        {
            var pool = new DisposeTrackingBufferPool();

            var writeSize = 512;

            var pipe = new Pipe(new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));

            // Write two blocks
            Memory<byte> buffer = pipe.Writer.GetMemory(writeSize);
            pipe.Writer.Advance(buffer.Length);
            pipe.Writer.GetMemory(buffer.Length);
            pipe.Writer.Advance(writeSize);
            await pipe.Writer.FlushAsync();

            Assert.Equal(2, pool.CurrentlyRentedBlocks);

            // Read everything
            ReadResult readResult = await pipe.Reader.ReadAsync();
            pipe.Reader.AdvanceTo(readResult.Buffer.End);

            // Try writing more
            await pipe.Writer.WriteAsync(new byte[writeSize]);

            Assert.Equal(1, pool.CurrentlyRentedBlocks);
            Assert.Equal(2, pool.DisposedBlocks);
        }

        [Fact]
        public async Task MultipleCompleteReaderWriterCauseDisposeOnlyOnce()
        {
            var pool = new DisposeTrackingBufferPool();

            var readerWriter = new Pipe(new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));
            await readerWriter.Writer.WriteAsync(new byte[] { 1 });

            readerWriter.Writer.Complete();
            readerWriter.Reader.Complete();
            Assert.Equal(1, pool.DisposedBlocks);

            readerWriter.Writer.Complete();
            readerWriter.Reader.Complete();
            Assert.Equal(1, pool.DisposedBlocks);
        }

        [Fact]
        public async Task RentsMinimumSegmentSize()
        {
            var pool = new DisposeTrackingBufferPool();
            var writeSize = 512;

            var pipe = new Pipe(new PipeOptions(pool, minimumSegmentSize: 2020, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));

            Memory<byte> buffer = pipe.Writer.GetMemory(writeSize);
            int allocatedSize = buffer.Length;
            pipe.Writer.Advance(buffer.Length);
            buffer = pipe.Writer.GetMemory(1);
            int ensuredSize = buffer.Length;
            await pipe.Writer.FlushAsync();

            pipe.Reader.Complete();
            pipe.Writer.Complete();

            Assert.Equal(2020, ensuredSize);
            Assert.Equal(2020, allocatedSize);
        }

        [Fact]
        public void ReturnsWriteHeadOnComplete()
        {
            var pool = new DisposeTrackingBufferPool();
            var pipe = new Pipe(new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));
            pipe.Writer.GetMemory(512);

            pipe.Reader.Complete();
            pipe.Writer.Complete();
            Assert.Equal(0, pool.CurrentlyRentedBlocks);
            Assert.Equal(1, pool.DisposedBlocks);
        }

        [Fact]
        public void ReturnsWriteHeadWhenRequestingLargerBlock()
        {
            var pool = new DisposeTrackingBufferPool();
            var pipe = new Pipe(new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));
            pipe.Writer.GetMemory(512);
            pipe.Writer.GetMemory(4096);

            pipe.Reader.Complete();
            pipe.Writer.Complete();
            Assert.Equal(0, pool.CurrentlyRentedBlocks);
            Assert.Equal(2, pool.DisposedBlocks);
        }

        [Fact]
        public async Task WriteDuringReadIsNotReturned()
        {
            var pool = new DisposeTrackingBufferPool();

            var writeSize = 512;

            var pipe = new Pipe(new PipeOptions(pool, readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false));
            await pipe.Writer.WriteAsync(new byte[writeSize]);

            pipe.Writer.GetMemory(writeSize);
            ReadResult readResult = await pipe.Reader.ReadAsync();
            pipe.Reader.AdvanceTo(readResult.Buffer.End);
            pipe.Writer.Write(new byte[writeSize]);
            await pipe.Writer.FlushAsync();

            Assert.Equal(1, pool.CurrentlyRentedBlocks);
        }
    }
}
