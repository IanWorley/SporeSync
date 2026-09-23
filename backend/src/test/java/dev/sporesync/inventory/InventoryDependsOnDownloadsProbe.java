package dev.sporesync.inventory;

import dev.sporesync.downloads.DownloadQueue;

public final class InventoryDependsOnDownloadsProbe {
  private final DownloadQueue dependency;

  public InventoryDependsOnDownloadsProbe(DownloadQueue dependency) {
    this.dependency = dependency;
  }
}
