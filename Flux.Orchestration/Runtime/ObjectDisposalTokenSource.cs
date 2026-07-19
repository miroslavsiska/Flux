namespace Flux.Orchestration.Runtime;

/// <summary>Owns and manages an <see cref="ObjectDisposalToken"/> and its linked disposal chain.</summary>
/// <remarks>Not reusable once disposed: accessing <see cref="Token"/> afterwards throws <see cref="ObjectDisposedException"/>.</remarks>
public class ObjectDisposalTokenSource : IDisposable
{
    /// <summary>Creates a source with an optional action invoked when its token is disposed.</summary>
    public ObjectDisposalTokenSource(Action<ObjectDisposalToken>? onDispose = null)
    {
        _token = new ObjectDisposalToken(null, onDispose);
    }

    private ObjectDisposalToken _token { get; init; }
    /// <summary>The disposal token. Throws <see cref="ObjectDisposedException"/> if the source is disposed.</summary>
    public ObjectDisposalToken Token
    {
        get
        {
            ThrowIfDisposed();
            return _token;
        }
    }

    /// <summary>Whether the source has been disposed.</summary>
    public bool IsDisposed => _isDisposed;

    /// <summary>Throws if the source has been disposed.</summary>
    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ObjectDisposalTokenSource), "This source has already been disposed.");
        }
    }

    private bool _isDisposed;

    /// <summary>Disposes the token source and its linked disposal chain.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources. Override in a derived class and call the base implementation.</summary>
    /// <param name="disposing"><see langword="true"/> to release managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;

            if (disposing)
            {
                _token.Dispose();
            }
        }
    }
}
