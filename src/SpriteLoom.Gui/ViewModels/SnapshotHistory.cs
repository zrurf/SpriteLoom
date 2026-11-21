namespace SpriteLoom.Gui.ViewModels;

internal sealed class SnapshotHistory<T>
{
    private const int Capacity = 200;
    private readonly List<T> _undo = [];
    private readonly List<T> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Push(T snapshot)
    {
        _undo.Add(snapshot);

        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public bool TryUndo(T current, out T snapshot)
    {
        if (_undo.Count == 0)
        {
            snapshot = default!;
            return false;
        }

        var index = _undo.Count - 1;
        snapshot = _undo[index];
        _undo.RemoveAt(index);
        _redo.Add(current);
        return true;
    }

    public bool TryRedo(T current, out T snapshot)
    {
        if (_redo.Count == 0)
        {
            snapshot = default!;
            return false;
        }

        var index = _redo.Count - 1;
        snapshot = _redo[index];
        _redo.RemoveAt(index);
        _undo.Add(current);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
