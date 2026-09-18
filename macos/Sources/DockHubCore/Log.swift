import Foundation
import os

/// Windows karsiligi: Core/Log.cs
public enum Log {
    private static let logger = Logger(subsystem: "com.dockhub.mac", category: "DockHub")

    public static func info(_ message: String) { logger.info("\(message, privacy: .public)") }
    public static func error(_ message: String, _ error: Error? = nil) {
        if let error { logger.error("\(message, privacy: .public): \(String(describing: error), privacy: .public)") }
        else { logger.error("\(message, privacy: .public)") }
    }
}
