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
    }
}
