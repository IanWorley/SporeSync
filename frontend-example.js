// Frontend JavaScript Example for SporeSync Upload Progress Tracking
// This shows how to connect to the SignalR hub and track upload progress

// Install: npm install @microsoft/signalr
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

class SporeSyncUploadTracker {
  constructor(apiBaseUrl = "https://localhost:5001") {
    this.apiBaseUrl = apiBaseUrl;
    this.connection = null;
    this.activeUploads = new Map();
    this.uploadProgressCallbacks = new Map();
  }

  // Initialize SignalR connection
  async initialize() {
    this.connection = new HubConnectionBuilder()
      .withUrl(`${this.apiBaseUrl}/hubs/fileupload`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Information)
      .build();

    // Set up event handlers for upload progress
    this.setupEventHandlers();

    try {
      await this.connection.start();
      console.log("Connected to SporeSync upload hub");

      // Join the group to receive all upload updates
      await this.connection.invoke("JoinAllUploadsGroup");
    } catch (err) {
      console.error("Failed to connect to SignalR hub:", err);
    }
  }

  // Set up SignalR event handlers
  setupEventHandlers() {
    this.connection.on("UploadStarted", (progressInfo) => {
      console.log("Upload started:", progressInfo);
      this.activeUploads.set(progressInfo.fileId, progressInfo);
      this.notifyProgressCallback(progressInfo.fileId, progressInfo);
    });

    this.connection.on("UploadProgress", (progressInfo) => {
      console.log(
        `Upload progress: ${progressInfo.progressPercentage.toFixed(1)}% - ${
          progressInfo.fileName
        }`
      );
      this.activeUploads.set(progressInfo.fileId, progressInfo);
      this.notifyProgressCallback(progressInfo.fileId, progressInfo);
    });

    this.connection.on("UploadCompleted", (progressInfo) => {
      console.log("Upload completed:", progressInfo.fileName);
      this.activeUploads.delete(progressInfo.fileId);
      this.notifyProgressCallback(progressInfo.fileId, progressInfo);
    });

    this.connection.on("UploadFailed", (progressInfo) => {
      console.error(
        "Upload failed:",
        progressInfo.fileName,
        progressInfo.errorMessage
      );
      this.activeUploads.delete(progressInfo.fileId);
      this.notifyProgressCallback(progressInfo.fileId, progressInfo);
    });

    this.connection.on("UploadCancelled", (progressInfo) => {
      console.log("Upload cancelled:", progressInfo.fileName);
      this.activeUploads.delete(progressInfo.fileId);
      this.notifyProgressCallback(progressInfo.fileId, progressInfo);
    });

    // Handle connection events
    this.connection.onreconnecting(() => {
      console.log("Reconnecting to upload hub...");
    });

    this.connection.onreconnected(() => {
      console.log("Reconnected to upload hub");
      // Rejoin the group after reconnection
      this.connection.invoke("JoinAllUploadsGroup");
    });
  }

  // Register callback for specific file upload progress
  onUploadProgress(fileId, callback) {
    this.uploadProgressCallbacks.set(fileId, callback);
  }

  // Remove callback for specific file
  removeUploadProgressCallback(fileId) {
    this.uploadProgressCallbacks.delete(fileId);
  }

  // Notify registered callbacks
  notifyProgressCallback(fileId, progressInfo) {
    const callback = this.uploadProgressCallbacks.get(fileId);
    if (callback && typeof callback === "function") {
      callback(progressInfo);
    }
  }

  // Start upload via API
  async startUpload(fileId, sshConfig, seedboxPath, remotePath) {
    try {
      const response = await fetch(
        `${this.apiBaseUrl}/api/filesync/${fileId}/upload`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            sshConfig,
            seedboxPath,
            remotePath,
          }),
        }
      );

      if (!response.ok) {
        throw new Error(`Upload failed: ${response.statusText}`);
      }

      const result = await response.json();
      return result;
    } catch (error) {
      console.error("Failed to start upload:", error);
      throw error;
    }
  }

  // Cancel upload via API
  async cancelUpload(fileId) {
    try {
      const response = await fetch(
        `${this.apiBaseUrl}/api/filesync/${fileId}/cancel`,
        {
          method: "POST",
        }
      );

      if (!response.ok) {
        throw new Error(`Cancel failed: ${response.statusText}`);
      }

      return await response.json();
    } catch (error) {
      console.error("Failed to cancel upload:", error);
      throw error;
    }
  }

  // Get current upload status
  async getUploadStatus(fileId) {
    try {
      const response = await fetch(
        `${this.apiBaseUrl}/api/filesync/${fileId}/status`
      );

      if (!response.ok) {
        if (response.status === 404) {
          return null; // Upload not found
        }
        throw new Error(`Status check failed: ${response.statusText}`);
      }

      return await response.json();
    } catch (error) {
      console.error("Failed to get upload status:", error);
      throw error;
    }
  }

  // Get all active uploads
  async getAllActiveUploads() {
    try {
      const response = await fetch(
        `${this.apiBaseUrl}/api/filesync/status/active`
      );

      if (!response.ok) {
        throw new Error(`Failed to get active uploads: ${response.statusText}`);
      }

      return await response.json();
    } catch (error) {
      console.error("Failed to get active uploads:", error);
      throw error;
    }
  }

  // Get files pending upload
  async getPendingFiles() {
    try {
      const response = await fetch(`${this.apiBaseUrl}/api/filesync/pending`);

      if (!response.ok) {
        throw new Error(`Failed to get pending files: ${response.statusText}`);
      }

      return await response.json();
    } catch (error) {
      console.error("Failed to get pending files:", error);
      throw error;
    }
  }

  // Disconnect from SignalR hub
  async disconnect() {
    if (this.connection) {
      await this.connection.stop();
      console.log("Disconnected from upload hub");
    }
  }

  // Utility method to format file size
  static formatFileSize(bytes) {
    const units = ["B", "KB", "MB", "GB", "TB"];
    let size = bytes;
    let unitIndex = 0;

    while (size >= 1024 && unitIndex < units.length - 1) {
      size /= 1024;
      unitIndex++;
    }

    return `${size.toFixed(1)} ${units[unitIndex]}`;
  }

  // Utility method to format time remaining
  static formatTimeRemaining(timeSpan) {
    if (!timeSpan) return "Unknown";

    const totalSeconds = Math.floor(timeSpan.totalSeconds || 0);
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;

    if (hours > 0) {
      return `${hours}h ${minutes}m ${seconds}s`;
    } else if (minutes > 0) {
      return `${minutes}m ${seconds}s`;
    } else {
      return `${seconds}s`;
    }
  }
}

// Usage Example
async function example() {
  const tracker = new SporeSyncUploadTracker("https://localhost:5001");

  try {
    // Initialize connection
    await tracker.initialize();

    // Set up progress callback for specific file
    tracker.onUploadProgress(123, (progressInfo) => {
      console.log(`File ${progressInfo.fileName}:`);
      console.log(`  Progress: ${progressInfo.progressPercentage.toFixed(1)}%`);
      console.log(
        `  Speed: ${SporeSyncUploadTracker.formatFileSize(
          progressInfo.uploadSpeedBytesPerSecond || 0
        )}/s`
      );
      console.log(
        `  ETA: ${SporeSyncUploadTracker.formatTimeRemaining(
          progressInfo.estimatedTimeRemaining
        )}`
      );

      // Update UI here
      updateProgressBar(progressInfo.fileId, progressInfo.progressPercentage);
      updateSpeedDisplay(
        progressInfo.fileId,
        progressInfo.uploadSpeedBytesPerSecond
      );
    });

    // Start an upload
    const uploadResult = await tracker.startUpload(
      123,
      {
        name: "My Seedbox",
        host: "seedbox.example.com",
        port: 22,
        username: "user",
        password: "password",
        authType: 0, // Password auth
      },
      "/home/user/downloads/file.mkv",
      "/remote/path/file.mkv"
    );

    console.log("Upload started:", uploadResult);

    // Get all pending files
    const pendingFiles = await tracker.getPendingFiles();
    console.log("Pending files:", pendingFiles);
  } catch (error) {
    console.error("Error:", error);
  }
}

// UI helper functions (implement these in your frontend)
function updateProgressBar(fileId, percentage) {
  // Update your progress bar UI element
  console.log(`Update progress bar for file ${fileId}: ${percentage}%`);
}

function updateSpeedDisplay(fileId, bytesPerSecond) {
  // Update speed display
  const speed = SporeSyncUploadTracker.formatFileSize(bytesPerSecond || 0);
  console.log(`Update speed display for file ${fileId}: ${speed}/s`);
}

// Export for use in modules
export default SporeSyncUploadTracker;
