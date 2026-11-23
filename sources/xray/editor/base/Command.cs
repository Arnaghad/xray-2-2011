namespace xray.editor_base
{
    // A rejected/throwing commit must leave its own state unchanged.
    // The engine compensates successfully committed nested commands on failure.
    public abstract class Command : System.IDisposable
    {
        public abstract bool commit();
        public abstract void rollback();
        public virtual bool AffectsDocument => true;

        public virtual void start_preview() { }
        public virtual bool end_preview() => true;
        public virtual void Dispose() { }
    }
}
