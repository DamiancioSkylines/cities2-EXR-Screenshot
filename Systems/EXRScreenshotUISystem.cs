using Colossal.UI.Binding;
using Game.Input;
using EXRScreenshot.Systems;

namespace EXRScreenshot.Systems
{
    internal partial class EXRScreenshotSystem : ExtendedUISystemBase
    {
        //private EXRScreenshotSystem _EXRScreenshotSystem;

        private const string ModID = "EXR Screenshot";
        private const string PanelToggle = "PanelToggle";
        private ProxyAction _toggleMainPanelBinding;
        private ValueBinding<bool> _panelVisibleBinding;
        //test git change
        protected override void OnCreate()
        {
            base.OnCreate();
            _toggleMainPanelBinding = Mod.MSetting.GetAction(nameof(Setting.KeyTakeScreenshot)); // Corrected to use Setting
            _toggleMainPanelBinding.shouldBeEnabled = true;

            _panelVisibleBinding = new ValueBinding<bool>(ModID, PanelToggle, false);
            AddBinding(_panelVisibleBinding);

            // set triggers
            AddBinding(new TriggerBinding<bool>(ModID, PanelToggle, SetPanelVisibility));
        }

        protected override void OnUpdate()
        {
            if (_toggleMainPanelBinding.WasPerformedThisFrame())
            {
                OnMainPanelToolTrigger();
            }

            base.OnUpdate();
        }

        private void SetPanelVisibility(bool open)
        {
            _panelVisibleBinding.Update(open);
            //Mod.m_Setting.InitializeProfiles();
            // _EXRScreenshotSystem.do_something();
        }

        private void OnMainPanelToolTrigger()
        {
            SetPanelVisibility(!_panelVisibleBinding.value);
        }
    }
}