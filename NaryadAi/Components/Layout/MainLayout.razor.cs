namespace NaryadAi.Components.Layout
{
    public partial class MainLayout
    {
        bool _drawerOpen = false;

        void ToggleDrawer()
        {
            _drawerOpen = !_drawerOpen;
        }
    }
}
