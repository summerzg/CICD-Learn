using System;
using System.Collections.Generic;
using System.Text;

namespace WebApplication1.Tests
{
  public class WeatherForecastTests
  {
    [Fact]
    public void TemperatureF_IsCalculatedFromCelsius()
    {
      var forecast = new WeatherForecast { TemperatureC = 0 };
      Assert.Equal(32, forecast.TemperatureF);
    }

    [Fact]
    public void GetAlert_ReturnsExtremeColdWarning_WhenTemperatureBelowMinusTen()
    {
      var service = new WeatherAlertService();
      var forecast = new WeatherForecast { TemperatureC = -11 };

      var result = service.GetAlert(forecast);

      Assert.Equal("Extreme cold warning", result);
    }

    [Fact]
    public void GetAlert_ReturnsExtremeHeatWarning_WhenTemperatureAboveForty()
    {
      var service = new WeatherAlertService();
      var forecast = new WeatherForecast { TemperatureC = 41 };

      var result = service.GetAlert(forecast);

      Assert.Equal("Extreme heat warning", result);
    }

    [Fact]
    public void GetAlert_ReturnsStormWarning_WhenSummaryIsStormy()
    {
      var service = new WeatherAlertService();
      var forecast = new WeatherForecast { TemperatureC = 20, Summary = "Stormy" };

      var result = service.GetAlert(forecast);

      Assert.Equal("Storm warning", result);
    }

    [Fact]
    public void GetAlert_ReturnsNoAlerts_WhenNoConditionMatches()
    {
      var service = new WeatherAlertService();
      var forecast = new WeatherForecast { TemperatureC = 20, Summary = "Sunny" };

      var result = service.GetAlert(forecast);

      Assert.Equal("No alerts.", result);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(39, true)]
    [InlineData(10, false)]
    public void ShouldCancelOutdoorEvent_ReturnsExpectedValue(int temperatureC, bool expected)
    {
      var service = new WeatherAlertService();
      var forecast = new WeatherForecast { TemperatureC = temperatureC };

      var result = service.ShouldCancelOutdoorEvent(forecast);

      Assert.Equal(expected, result);
    }
  }
}
