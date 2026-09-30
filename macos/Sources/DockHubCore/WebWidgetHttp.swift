import Foundation

/// dockhub.http.request: Web widget'lar icin native HTTP istek motoru.
/// Windows karsiligi: Widgets/Web/WebWidgetHttp.cs
public enum WebWidgetHttp {
    public static let maxResponseBytes = 1024 * 1024 // 1 MB
    public static let maxBodyBytes = 64 * 1024       // 64 KB
    public static let methods = Set(["GET", "POST", "PUT", "DELETE"])
    public static let forbiddenHeaders = Set([
        "host", "cookie", "connection", "content-length", "transfer-encoding", "upgrade", "proxy-authorization"
    ])

    public struct Request: Sendable {
        public let url: URL
        public let method: String
        public let headers: [(String, String)]
        public let body: String?

        public init(url: URL, method: String = "GET", headers: [(String, String)] = [], body: String? = nil) {
            self.url = url
            self.method = method
            self.headers = headers
            self.body = body
        }
    }

    public struct Response: Sendable {
        public let status: Int
        public let ok: Bool
        public let contentType: String?
        public let body: String

        public init(status: Int, ok: Bool, contentType: String?, body: String) {
            self.status = status
            self.ok = ok
            self.contentType = contentType
            self.body = body
        }

        public var asJSON: JSONValue {
            var dict: [String: JSONValue] = [
                "status": .number(Double(status)),
                "ok": .bool(ok),
                "body": .string(body),
            ]
            if let ct = contentType {
                dict["contentType"] = .string(ct)
            } else {
                dict["contentType"] = .null
            }
            return .object(dict)
        }
    }

    /// JSON argumanlarini ayristirir ve guvenlik denetimlerini yapar.
    public static func parse(args: [String: JSONValue]?, isHostAllowed: (String) -> Bool) throws -> Request {
        guard let urlString = args?["url"]?.stringValue, !urlString.isEmpty else {
            throw WebWidgetError("url is required")
        }
        guard let url = URL(string: urlString), let scheme = url.scheme?.lowercased(),
              scheme == "http" || scheme == "https" else {
            throw WebWidgetError("Only http and https addresses can be requested.")
        }
        guard let host = url.host, isHostAllowed(host) else {
            throw WebWidgetError("\(url.host ?? "") is not in the widget's permissions.")
        }

        let method = (args?["method"]?.stringValue ?? "GET").uppercased()
        guard methods.contains(method) else {
            throw WebWidgetError("Method \(method) is not supported.")
        }

        var headers: [(String, String)] = []
        if case .object(let headerDict)? = args?["headers"] {
            for (key, val) in headerDict {
                if forbiddenHeaders.contains(key.lowercased()) {
                    throw WebWidgetError("The \(key) header can't be set.")
                }
                headers.append((key, val.stringValue ?? ""))
            }
        }

        let body = args?["body"]?.stringValue
        if let body, body.utf8.count > maxBodyBytes {
            throw WebWidgetError("The request body is larger than 64 KB.")
        }

        return Request(url: url, method: method, headers: headers, body: body)
    }

    /// Istegi gonderir; yanit en fazla 1 MB metin icerebilir.
    public static func send(_ request: Request) async throws -> Response {
        var urlRequest = URLRequest(url: request.url, timeoutInterval: 15)
        urlRequest.httpMethod = request.method
        urlRequest.httpShouldHandleCookies = false

        for (key, val) in request.headers {
            urlRequest.addValue(val, forHTTPHeaderField: key)
        }

        if let body = request.body {
            urlRequest.httpBody = Data(body.utf8)
        }

        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 15
        config.timeoutIntervalForResource = 15
        config.httpCookieAcceptPolicy = .never
        config.httpShouldSetCookies = false

        let session = URLSession(configuration: config, delegate: NoRedirectDelegate(), delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }

        let (data, response) = try await session.data(for: urlRequest)

        guard let httpResponse = response as? HTTPURLResponse else {
            throw WebWidgetError("Invalid HTTP response")
        }

        if data.count > maxResponseBytes {
            throw WebWidgetError("The answer is larger than 1 MB.")
        }

        let bodyString = String(decoding: data, as: UTF8.self)
        let ok = (200...299).contains(httpResponse.statusCode)
        let contentType = httpResponse.value(forHTTPHeaderField: "Content-Type")

        return Response(
            status: httpResponse.statusCode,
            ok: ok,
            contentType: contentType,
            body: bodyString
        )
    }

    private final class NoRedirectDelegate: NSObject, URLSessionTaskDelegate, @unchecked Sendable {
        func urlSession(
            _ session: URLSession,
            task: URLSessionTask,
            willPerformHTTPRedirection response: HTTPURLResponse,
            newRequest request: URLRequest,
            completionHandler: @escaping (URLRequest?) -> Void
        ) {
            // Yonlendirmeleri takip etme; sunucunun 3xx cevabini oldugu gibi dondur.
            completionHandler(nil)
        }
    }
}

public struct WebWidgetError: LocalizedError, Sendable {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

extension JSONValue {
    public var stringValue: String? {
        if case .string(let s) = self { return s }
        return nil
    }
    public var boolValue: Bool? {
        if case .bool(let b) = self { return b }
        return nil
    }
    public var numberValue: Double? {
        if case .number(let n) = self { return n }
        return nil
    }
}
