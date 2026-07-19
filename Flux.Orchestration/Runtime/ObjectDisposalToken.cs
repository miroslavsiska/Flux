using System.Collections.Concurrent;

namespace Flux.Orchestration.Runtime;

/// <summary>Manages the disposal of an associated object and its linked child tokens as a hierarchy.</summary>
/// <remarks>
/// Disposing a token disposes all linked child tokens. Instances implementing <see cref="IDisposable"/> or
/// <see cref="IAsyncDisposable"/> are disposed via the DisposeWithInstances[Async] methods.
/// </remarks>
public struct ObjectDisposalToken
{
    private readonly ConcurrentBag<ObjectDisposalToken> _children = [];
    private readonly Action<ObjectDisposalToken>? _onDispose;
    private bool _isDisposed;

   /// <summary>Creates a token for an object, with an optional action invoked on disposal.</summary>
   /// <param name="instance">The object managed by this token. Can be <see langword="null"/>.</param>
   /// <param name="onDispose">Optional action invoked when the token is disposed.</param>
    public ObjectDisposalToken(object? instance, Action<ObjectDisposalToken>? onDispose = null)
    {
        Instance = instance;
        _onDispose = onDispose;
    }

    /// <summary>The linked child tokens.</summary>
    public IEnumerable<ObjectDisposalToken> Children => _children;

    /// <summary>The object instance associated with this token.</summary>
    public object? Instance { get; }

    /// <summary>Creates a child token linked to the given instance. Throws if this token is disposed.</summary>
    /// <param name="instance">The object to associate with the new token.</param>
    /// <param name="onDispose">Optional action invoked when the new token is disposed.</param>
    /// <returns>The new linked child token.</returns>
    public ObjectDisposalToken CreateLinkedDisposalToken(object instance, Action<ObjectDisposalToken>? onDispose = null)
    {
        ThrowIfDisposed();

        var token = new ObjectDisposalToken(instance, onDispose);
        AddChild(token);
        return token;
    }

    /// <summary>Disposes this token and all linked instances that implement <see cref="IDisposable"/>.</summary>
    public void DisposeWithInstances()
    {
        foreach (var i in _children)
        {
            if (i.Instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
            i.DisposeWithInstances();
        }
        
        this.Dispose(); 
    }

    /// <summary>Asynchronously disposes this token and all linked instances.</summary>
    /// <remarks>
    /// Children are disposed in parallel, preferring <see cref="IAsyncDisposable"/> then <see cref="IDisposable"/>.
    /// A failure disposing one child is swallowed so the rest still run. No-op if already disposed.
    /// </remarks>
    public async ValueTask DisposeWithInstancesAsync()
    {
        if (_isDisposed) return;

        // 1. Připravíme kolekci Tasků (ne ValueTasků, aby WhenAll fungoval)
        // Select vytvoří IEnumerable<Task>
        var tasks = _children.Select(async child =>
        {
            try
            {
                // The disposal of each child instance is attempted, and any exceptions
                // that occur during this process are caught and logged.
                if (child.Instance is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (child.Instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                // Recursively dispose of any child tokens linked to this child instance,
                // ensuring that the entire hierarchy of linked instances is properly disposed of.
                await child.DisposeWithInstancesAsync();
            }
            catch (Exception ex)
            {
                // but do not fail the entire disposal process.
                // We want to attempt to dispose of all children even if one fails,
                // and log any exceptions that occur during disposal for later analysis.

                // TODO: trace logging for failed disposal of a child instance,
                // _logger?.LogError(ex, "Failed to dispose child instance");
            }
        });
      
        await Task.WhenAll(tasks);

        this.Dispose();
    }

    private void InvokeOnDisposed() => Disposed?.Invoke(this);
    private void AddChild(ObjectDisposalToken token) => _children.Add(token);

    /// <summary>Throws if the token has been disposed.</summary>
    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ObjectDisposalToken), "This token has already been disposed.");
        }
    }

    public event Action<ObjectDisposalToken>? Disposed;
    private void InvokeOnDispose() => _onDispose?.Invoke(this);


    /// <summary>Disposes this token, invoking the onDispose action and disposing all child tokens.</summary>
    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;

            InvokeOnDispose();

            foreach (var child in _children)
            {
                child.Dispose();
            }

            InvokeOnDisposed();
        }
    }
}
