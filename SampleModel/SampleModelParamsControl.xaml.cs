namespace OptionsModels.SampleModel
{
    /// <summary>
    /// Контрол для настройки параметров SampleModel
    /// </summary>
    public partial class SampleModelParamsControl
    {
        #region ctor

        public SampleModelParamsControl(SampleModel model)
        {
            InitializeComponent();
            
            txtVolaShift.DataContext = model;
        }

        #endregion
    }
}