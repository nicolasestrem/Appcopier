namespace Views
{
    /// <summary>
    /// A view that reads its content from disk and must re-read it every time it is shown.
    /// </summary>
    /// <remarks>
    /// Home summarises the backup folders and their manifests. Those change while the app is running -
    /// running a backup is the single most likely thing a user does between two visits to Home - so a
    /// view built once at construction would show a stale answer to the one question the screen exists
    /// to answer.
    ///
    /// An interface rather than NavigationService knowing about HomePageView: navigation should not
    /// grow a list of view types it has to special-case as more screens land in PRs 6-8.
    /// </remarks>
    internal interface IRefreshableView
    {
        void RefreshView();
    }
}
