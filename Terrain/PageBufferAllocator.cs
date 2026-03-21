#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Threading;

namespace Terrain;

internal sealed unsafe class NativePageBufferOwner : MemoryManager<byte>
{
    private PageBufferAllocator? allocator;
    private byte* pointer;
    private readonly int length;

    public NativePageBufferOwner(PageBufferAllocator allocator, byte* pointer, int length)
    {
        this.allocator = allocator;
        this.pointer = pointer;
        this.length = length;
    }

    public override Span<byte> GetSpan()
    {
        if (pointer == null)
        {
            throw new ObjectDisposedException(nameof(NativePageBufferOwner));
        }

        return new Span<byte>(pointer, length);
    }

    public override MemoryHandle Pin(int elementIndex = 0)
    {
        if ((uint)elementIndex > (uint)length)
        {
            throw new ArgumentOutOfRangeException(nameof(elementIndex));
        }

        return new MemoryHandle(pointer + elementIndex);
    }

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (pointer == null)
        {
            return;
        }

        allocator!.Return(pointer);
        pointer = null;
        allocator = null;
    }
}

internal sealed unsafe class PageBufferAllocator : IDisposable
{
    private readonly int bytesPerPage;
    private readonly int maxCount;
    private readonly ConcurrentBag<nint> pool = new();
    private int allocated;

    public PageBufferAllocator(int bytesPerPage, int maxCount)
    {
        this.bytesPerPage = bytesPerPage;
        this.maxCount = maxCount;
    }

    public IMemoryOwner<byte> Rent()
    {
        if (pool.TryTake(out nint pointer))
        {
            return new NativePageBufferOwner(this, (byte*)pointer, bytesPerPage);
        }

        if (Interlocked.Increment(ref allocated) <= maxCount)
        {
            nint memory = (nint)NativeMemory.Alloc((nuint)bytesPerPage);
            return new NativePageBufferOwner(this, (byte*)memory, bytesPerPage);
        }

        Interlocked.Decrement(ref allocated);
        throw new OutOfMemoryException($"Page buffer pool exhausted. Max buffers: {maxCount}.");
    }

    public bool TryRent([NotNullWhen(true)] out IMemoryOwner<byte>? owner)
    {
        if (pool.TryTake(out nint pointer))
        {
            owner = new NativePageBufferOwner(this, (byte*)pointer, bytesPerPage);
            return true;
        }

        if (Interlocked.Increment(ref allocated) <= maxCount)
        {
            nint memory = (nint)NativeMemory.Alloc((nuint)bytesPerPage);
            owner = new NativePageBufferOwner(this, (byte*)memory, bytesPerPage);
            return true;
        }

        Interlocked.Decrement(ref allocated);
        owner = null;
        return false;
    }

    internal void Return(byte* pointer)
        => pool.Add((nint)pointer);

    public void Dispose()
    {
        while (pool.TryTake(out nint pointer))
        {
            NativeMemory.Free((void*)pointer);
        }
    }
}
