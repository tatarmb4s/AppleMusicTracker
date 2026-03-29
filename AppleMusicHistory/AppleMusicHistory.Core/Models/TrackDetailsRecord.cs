namespace AppleMusicHistory.Core.Models;

public sealed record TrackDetailsRecord(
    TrackRecord Track,
    TrackMetadataRecord? Metadata);
