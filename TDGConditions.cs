using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class SIYLConditions : Indicator
    {
        #region Variables
        // Market Data & Key Values
        private double priorLow;
        private double priorHigh;
        private double currentLow;
        private double currentHigh;
        private ATR dailyATR;
        private EMA ema233;

        // Status / Output
        private double volatilityPercent = 0;
        private string volatilityStatus = "";
        private string rangePosition = "";
        private string trendStatus = "";
        private string tlineBias = "";
        private double lastPrice;
        #endregion

        #region Properties
        [Range(1, int.MaxValue), NinjaScriptProperty]
        [Display(Name = "ATR Period", Description = "Period for ATR calculation", Order = 1, GroupName = "Parameters")]
        public int ATRPeriod { get; set; }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"TDG - Trading Conditions Analyzer: To give a quick general view of market condition in real time.";
                Name = "TDGConditions";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // Default values
                ATRPeriod = 7;
            }
            else if (State == State.Configure)
            {
                // Add daily data series for calculations
                AddDataSeries(BarsPeriodType.Day, 1);
            }
            else if (State == State.DataLoaded)
            {
                dailyATR = ATR(BarsArray[1], 7);
                ema233 = EMA(Input, 233);
            }
        }

        #region Core Calculations
        //Get LAST PRICE
        protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate.MarketDataType == MarketDataType.Last)
            {
                lastPrice = marketDataUpdate.Price;
            }
        }
        
        private void CalculateVolatility()
        {
            // Make sure we have enough daily bars
            if (CurrentBars[1] < ATRPeriod)
                return;

            double atrValue = dailyATR[0];

            // Make sure ATR is not zero to avoid division by zero
            if (atrValue.ApproxCompare(0) == 0)
                return;

            // Use daily high and low for BarsInProgress=1 series
            currentHigh = Highs[1][0];
            currentLow  = Lows[1][0];

            volatilityPercent = (currentHigh - currentLow) / atrValue * 100;
            //Debug output
            //Print($"Volatility %: {volatilityPercent:F2}%");
        }

        private void DetermineRangePositionAndTrend()
        {
            // Ensure we have at least 2 daily bars (today + prior day)
            if (CurrentBars[1] < 2)
                return;
            
            // Get PRIOR high and low
            double priorHigh = Highs[1][1];
            double priorLow = Lows[1][1];

            // Check if current price is INSIDE yesterday's range
            // For "Inside Day": PRICE <= Yesterday's HIGH AND >= Yesterday's LOW
            // For "Outside Day": PRICE > Yesterday's HIGH OR < Yesterday's LOW

            rangePosition = lastPrice <= priorHigh && lastPrice >= priorLow ? "Inside" : "Outside";
            
            //IF OUTSIDE: check if we are above/below prior range extreme... NOT SURE HOW TO logically determine/calculate trend
            //I had an idea where we look at prior 7 sessions, and count if close was above prior high 4/7 days for up trend, below low for downtrend and within range 'neutral/choppy'
            //then I thought some sort of recent bias would be good 4 days neutral, but 3 newer consecutive days in uptrend = uptrend, but thats for another day.
            //continuing this thought - past week price action would go outside the top/bottom range then revert to the mean - sure bullish (above tline) but no follow through, also no real downward pressure - food for thought
            if (lastPrice > priorHigh)
                trendStatus = "Up";
            else if (lastPrice < priorLow)
                trendStatus = "Down";
            else 
                trendStatus = "Neutral";
            
            // Debug output
            //Print($"Last Price: {lastPrice} | Prior High: {priorHigh} | Prior Low: {priorLow} | Range: {rangePosition} | Trend: {trendStatus}");
        }
        private string GetTLineBias()
        {
            //Doesn't match up exactly to 233 on the market analyzer but within margin of error. 
            double emaValue = ema233[0];

            if (lastPrice > emaValue)
                return tlineBias = "Bullish";
            else
                return tlineBias = "Bearish";
        }

        private string GetTradingConditions()
        {
            // Simple logic for beginners to understand favorable conditions for trading
            bool isVolatileEnough = volatilityPercent >= 50;
            bool hasClearTrend = trendStatus != "Neutral";

            if (!isVolatileEnough)
                return "❌ Low Volatility";
            else if (hasClearTrend)
                return "✅ Good Conditions";
            else
                return "⚠️ Choppy Market";
        }
        #endregion

        protected override void OnBarUpdate()
        {
            //BUG: STARTS OFF WITH OUTSIDE DAY - DUE TO LAST PRICE - outside of cash session it may delay to be accurate, but once last price is trigger should be correct. maybe != lastPrice then AVG bid/ask, but not breaking at the moment.
            //SERIES OF CHECKS TO VALIDATE DATA IS LOADED FOR CALCULATIONS
            // Only calculate if we have at least one daily bar
            if (CurrentBars[1] < 1)
                return;

            // Only calculate ATR if enough daily bars exist
            if (CurrentBars[1] < ATRPeriod)
                return;

            // Only calculate prior day info if we have at least 1 prior day
            if (CurrentBars[1] < 2)
                return;
            
            if (CurrentBars[0] < ema233.Period)
                return;
            
            // Determine conditions on intraday bars
            DetermineRangePositionAndTrend();
            CalculateVolatility();
            GetTLineBias();
                
            // Build output text
            string instrument = Instrument.MasterInstrument.Name;
            string volatilityText = $"{volatilityPercent:F2}%";

            string outputText = $"{instrument}\n";
            outputText += $"Volatility: {volatilityText} - {volatilityStatus}\n";
            outputText += $"{tlineBias}: {rangePosition} Day | Trend: {trendStatus}\n";
            outputText += $"Conditions: {GetTradingConditions()}";

            // Display on chart
            RemoveDrawObject("Status");
            Draw.TextFixed(this, "Status", outputText, TextPosition.BottomRight);
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private SIYLConditions[] cacheSIYLConditions;
		public SIYLConditions SIYLConditions(int aTRPeriod)
		{
			return SIYLConditions(Input, aTRPeriod);
		}

		public SIYLConditions SIYLConditions(ISeries<double> input, int aTRPeriod)
		{
			if (cacheSIYLConditions != null)
				for (int idx = 0; idx < cacheSIYLConditions.Length; idx++)
					if (cacheSIYLConditions[idx] != null && cacheSIYLConditions[idx].ATRPeriod == aTRPeriod && cacheSIYLConditions[idx].EqualsInput(input))
						return cacheSIYLConditions[idx];
			return CacheIndicator<SIYLConditions>(new SIYLConditions(){ ATRPeriod = aTRPeriod }, input, ref cacheSIYLConditions);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.SIYLConditions SIYLConditions(int aTRPeriod)
		{
			return indicator.SIYLConditions(Input, aTRPeriod);
		}

		public Indicators.SIYLConditions SIYLConditions(ISeries<double> input , int aTRPeriod)
		{
			return indicator.SIYLConditions(input, aTRPeriod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.SIYLConditions SIYLConditions(int aTRPeriod)
		{
			return indicator.SIYLConditions(Input, aTRPeriod);
		}

		public Indicators.SIYLConditions SIYLConditions(ISeries<double> input , int aTRPeriod)
		{
			return indicator.SIYLConditions(input, aTRPeriod);
		}
	}
}

#endregion
