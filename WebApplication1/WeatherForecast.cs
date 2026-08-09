namespace WebApplication1
{
    public class WeatherForecast
    {
        public DateOnly Date { get; set; }

        public int TemperatureC { get; set; }

        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        public string GetComfortLevel()
        {
            if (TemperatureC <= 0)
            {
                return "Cold";
            }

            if (TemperatureC >= 30)
            {
                return "Hot";
            }

            return "Moderate";
        }

        public string? Summary { get; set; }
    }
}
