namespace WebApplication1
{
  // Intentionally untested to demonstrate coverage gate failure.
  public class WeatherAlertService
  {
    public string GetAlert(WeatherForecast forecast)
    {
      if (forecast.TemperatureC < -10)
        return "Extreme cold warning";

      if (forecast.TemperatureC > 40)
        return "Extreme heat warning";

      if (forecast.Summary == "Stormy")
        return "Storm warning";

      return "No alerts";
    }

    public bool ShouldCancelOutdoorEvent(WeatherForecast forecast)
    {
      return forecast.TemperatureC < 0 || forecast.TemperatureC > 38;
    }
  }
}
