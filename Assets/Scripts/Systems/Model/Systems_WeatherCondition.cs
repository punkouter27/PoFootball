namespace PoFootball.Models
{
    /// <summary>
    /// What the sky is doing over the stadium, reduced to the five things the
    /// broadcast can draw differently. A WMO weather code has a hundred values;
    /// drizzle, showers and a thunderstorm all look like rain from forty metres up.
    ///
    /// PRESENTATION ONLY. Nothing a brain observes reads this: rain darkens the
    /// lights and puts streaks over the picture, and the ball flies exactly as it
    /// would on a clear night. Making it slippery would be a contract revision.
    /// </summary>
    public enum Systems_WeatherCondition
    {
        Clear = 0,
        Cloudy = 1,
        Rain = 2,
        Snow = 3,
        Fog = 4,
    }
}
