import Foundation
import DockHubCore

public struct WeatherReading: Sendable {
    public let temperatureC: Double
    public let weatherCode: Int
    public let place: String?

    /// WMO hava durumu kodu -> SF Symbol adi ve kisa Turkce aciklama.
    /// Open-Meteo WMO kod tablosunu kullanir.
    public var symbolName: String {
        switch weatherCode {
        case 0:            "sun.max.fill"
        case 1, 2:         "cloud.sun.fill"
        case 3:            "cloud.fill"
        case 45, 48:       "cloud.fog.fill"
        case 51...57:      "cloud.drizzle.fill"
        case 61...67:      "cloud.rain.fill"
        case 71...77:      "cloud.snow.fill"
        case 80...82:      "cloud.heavyrain.fill"
        case 85, 86:       "cloud.snow.fill"
        case 95...99:      "cloud.bolt.rain.fill"
        default:           "questionmark.circle"
        }
    }

    public var description: String {
        // Windows karsiligi: Services/WeatherService.cs DescribeCode (birebir).
        switch weatherCode {
        case 0:            "Clear sky"
        case 1:            "Mainly clear"
        case 2:            "Partly cloudy"
        case 3:            "Overcast"
        case 45, 48:       "Fog"
        case 51, 53, 55:   "Drizzle"
        case 56, 57:       "Freezing drizzle"
        case 61:           "Slight rain"
        case 63:           "Moderate rain"
        case 65:           "Heavy rain"
        case 66, 67:       "Freezing rain"
        case 71:           "Slight snow"
        case 73:           "Moderate snow"
        case 75:           "Heavy snow"
        case 77:           "Snow grains"
        case 80, 81:       "Rain showers"
        case 82:           "Violent rain showers"
        case 85, 86:       "Snow showers"
        case 95:           "Thunderstorm"
        case 96, 99:       "Thunderstorm with hail"
        default:           "—"
        }
    }
}

public enum WeatherError: Error, Sendable {
    /// Konum bilgisi yok. UYDURMA DEGER GOSTERILMEZ; durum kullaniciya bildirilir.
    case noLocation
    case network(String)
    case badResponse
}

/// Open-Meteo. Windows karsiligi: Services/WeatherService.cs - ayni saglayici,
/// API anahtari gerektirmez. Eslestirme: platformdan bagimsiz HTTP.
public enum WeatherService {
    public static func fetch(latitude: Double, longitude: Double,
                             place: String?) async throws -> WeatherReading {
        var c = URLComponents(string: "https://api.open-meteo.com/v1/forecast")!
        c.queryItems = [
            .init(name: "latitude", value: String(latitude)),
            .init(name: "longitude", value: String(longitude)),
            .init(name: "current", value: "temperature_2m,weather_code"),
            .init(name: "timezone", value: "auto"),
        ]
        guard let url = c.url else { throw WeatherError.badResponse }

        let (data, response): (Data, URLResponse)
        do { (data, response) = try await URLSession.shared.data(from: url) }
        catch { throw WeatherError.network(error.localizedDescription) }

        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw WeatherError.badResponse
        }
        guard let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let current = root["current"] as? [String: Any],
              let temp = current["temperature_2m"] as? Double,
              let code = current["weather_code"] as? Int
        else { throw WeatherError.badResponse }

        return WeatherReading(temperatureC: temp, weatherCode: code, place: place)
    }
}
