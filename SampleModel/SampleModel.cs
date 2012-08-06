using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using ITGlobal.Dealer.Common.Messages;
using OptionsWorkshop.InstrumentsManagement;
using OptionsWorkshop.PositionsManagement;
using OptionsWorkshop.Pricing;

namespace OptionsModels.SampleModel
{
    /// <summary>
    /// Пример реализации модели ценообразования опционов
    /// Для расчетов используется модель Black-Scholes с возможностью сдвига кривой волатильности на константу (вверх/вниз)
    /// </summary>
    [Export(typeof(IModel))]
    public class SampleModel : IModel
    {
        #region Fields

        /// <summary>
        /// Сдвиг кривой волатильности (в пунктах волатильности).
        /// </summary>
        private decimal modelVolaShift;

        #endregion

        #region Properties

        /// <summary>
        /// Свойство для биндинга в контрол и для хранения временного результата (до ApplyChanges).
        /// </summary>
        public decimal VolaShiftTemp { get; set; }

        #endregion

        #region Implementation of IModel

        /// <summary>
        /// Серия опционов
        /// </summary>
        public OptionsSeries OptionsSeries { get; set; }

        /// <summary>
        /// Интерфейс для получения параметров инструментов.
        /// </summary>
        public IInstrumentParamsProvider InstrumentParamsProvider { get; set; }

        /// <summary>
        /// Интерфейс для получения позиций.
        /// </summary>
        public IPositionsProvider Positions { get; set; }

        /// <summary>
        /// Контрол для настройки параметров модели
        /// </summary>
        public object ModelParamsControl
        {
            get
            {
                // Подкидываем в свойство для биндинга последнее подтверждённое значение
                VolaShiftTemp = modelVolaShift;

                return new SampleModelParamsControl(this);
            }
        }

        /// <summary>
        /// Отображаемое название модели.
        /// </summary>
        public string Name
        {
            get { return "Sample"; }
        }

        /// <summary>
        /// Расчёт цены опциона.
        /// </summary> 
        /// <param name="ip">
        /// Параметры инструмента (опциона).
        /// </param>
        /// <param name="oep">
        /// Параметры расчета опциона.
        /// </param>
        public double CalcPrice(InstrumentParams ip, OptionEvaluationParams oep)
        {
            // Проверяем переданные параметры расчёта опциона.
            if (!PopulateOptionEvaluationParams(ip, ref oep))
                return 0; // Расчёт по переданным параметрам невозможен.
            
            Debug.Assert(oep.BaseActivePrice != null, "oep.BaseActivePrice != null");
            Debug.Assert(oep.Time != null, "oep.Time != null");
            Debug.Assert(oep.Vola != null, "oep.Vola != null");
            Debug.Assert(oep.VolaShift != null, "oep.VolaShift != null");

            // Рассчитываем количество лет до экспирации опциона. 
            var T = GetYearsTillExpiration(ip, oep.Time.Value);

            // Считаем цену опциона через модель Black-Scholes
            return BlackScholes.Price(ip.OptionType, 
                                      oep.BaseActivePrice.Value, 
                                      (double)ip.Strike, 
                                      T,
                                      0, // TODO Добавить ставку в OptionEvaluationParams
                                      oep.Vola.Value + oep.VolaShift.Value);
        }

        /// <summary>
        /// Расчёт расширенных (теоретических) параметров опциона.
        /// </summary> 
        /// <param name="ip">
        /// Параметры инструмента (опциона).
        /// </param>
        /// <param name="oep">
        /// Параметры расчета опциона.
        /// </param>
        InstrumentCalculatedParams IModel.CalcPriceAndGreeks(InstrumentParams ip, OptionEvaluationParams oep)
        {
            // Создаём шаблон результата
            var rValue = new InstrumentCalculatedParams(ip);

            // Проверяем переданные параметры расчёта опциона.
            if (!PopulateOptionEvaluationParams(ip, ref oep))
                return rValue; // Расчёт по переданным параметрам невозможен.

            Debug.Assert(oep.BaseActivePrice != null, "oep.BaseActivePrice != null");
            Debug.Assert(oep.Time != null, "oep.Time != null");
            Debug.Assert(oep.Vola != null, "oep.Vola != null");
            Debug.Assert(oep.VolaShift != null, "oep.VolaShift != null");

            // Рассчитываем количество лет до экспирации опциона. 
            var T = GetYearsTillExpiration(ip, oep.Time.Value);

            // Сохраняем цену базового актива для которой выполняется расчёт
            rValue.BaseAssetPrice = oep.BaseActivePrice.Value;
            // Сохраняем волатильность для которой выполняется расчёт
            rValue.TheorIV = oep.Vola.Value + oep.VolaShift.Value;
            // Сохраняем дату и время для которых выполняется расчёт
			// Этого свойства пока нет в публичной версии (будет в 12.8)
			//rValue.Time = oep.Time;

            // Считаем цену и греки через модель Black-Scholes
            var all = BlackScholes.CalcPriceAndGreeks(ip.OptionType, 
                                                      oep.BaseActivePrice.Value, 
                                                      (double)ip.Strike, 
                                                      T,
                                                      0, // TODO Добавить ставку в OptionEvaluationParams
                                                      oep.Vola.Value + oep.VolaShift.Value);

            // Сохраняем цену и греки
            rValue.TheorPrice = all[0];
            rValue.Delta = all[1];
            rValue.Gamma = all[2];
            rValue.Vega = all[3];
            rValue.Theta = all[4];

            return rValue;
        }

        /// <summary>
        /// Получение волатильности используемой в модели для опциона.
        /// </summary>
        /// <param name="ip">Параметры инструмента.</param>
        /// <returns>Волатильность.</returns>
        public decimal GetVola(InstrumentParams ip)
        {
            return ip.Volty + modelVolaShift; // Берём сдвиг волатильности заданный через контрол настройки
        }

        /// <summary>
        /// Применение настроек пользователя к расчётам.
        /// </summary>
        void IModel.ApplyChanges()
        {
            modelVolaShift = VolaShiftTemp;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Проверка и заполнение параметров расчёта опциона.
        /// </summary>
        /// <param name="ip">Параметры инструмента (опциона).</param>
        /// <param name="oep">Парамметры расчёта опциона.</param>
        /// <returns>true если параметры валидны, false если расчёт по данным параметрам невозможен.</returns>
        private bool PopulateOptionEvaluationParams(InstrumentParams ip, ref OptionEvaluationParams oep)
        {
            #region Цена базового актива

            // Если не указана конкретная цена базового актива по которой нужно выполнять расчёт
            if (!oep.BaseActivePrice.HasValue)
            {
                // Пытаемся получить параметры фьючерса для данного опциона
                var ba = InstrumentParamsProvider.GetOptionFuturesParams(ip.Instrument);

                if (ba == null) // Если параметры фьючерса не получены - мы не можем выполнить расчёт
                    return false;

                oep.BaseActivePrice = ba.GetLastPriceOrSettlement(); // Получаем последнюю цену фьючерса
            }

            #endregion

            #region Время

            if (!oep.Time.HasValue)
            {
                // Если не указана точка во времени для которой нужно рассчитать цену опциона выполняем расчёт для текущего времени
                oep.Time = DateTime.Now;
            }

            #endregion

            #region Волатильность

            // Если не указана волатильность для которой нужно выполнять расчёт
            if (!oep.Vola.HasValue)
            {
                oep.Vola = (double)GetVola(ip); // Запрашиваем волатильность от этой же модели
            }

            #endregion

            #region Сдвиг волатильности

            // Если не задан сдвиг волатильности для которого нужно выполнять расчёт
            if (!oep.VolaShift.HasValue)
            {
                oep.VolaShift = 0;
            }

            #endregion

            return true; // Все параметры заполнены, можно производить расчёт
        }

        private static double GetYearsTillExpiration(InstrumentParams ip, DateTime dateTime)
        {
            // HARDCODE: Время окончания сессии (экспирации опционов) на FORTS
            return BlackScholes.YearsBetweenDates(dateTime, ip.ExpirationDate.Date.AddHours(18).AddMinutes(45));
        }

        #endregion
    }
}