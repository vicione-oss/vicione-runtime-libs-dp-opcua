# OPC UA Publishing Interval

## Overview

The **Publishing Interval** is a server-side timer that controls how often the server collects queued notifications from monitored items and sends them to the client in a Publish Response. It does **not** control when values are written or when changes are detected.

## Data Flow

```
Write (immediate)
    │
    ▼
Sampling (MonitoredItem detects the change; sampling interval, often 0 = immediate on change)
    │
    ▼
Notification queued (change is placed in the MonitoredItem's queue)
    │
    ▼
Publishing Interval tick (server checks all items in the subscription,
                          batches queued notifications,
                          sends Publish Response to client)
```

1. **Write** — happens immediately via the OPC UA Write service. No interval is involved.
2. **Sampling** — the MonitoredItem detects the change. The sampling interval determines how often the item checks for new values. A value of 0 means "report on change" (event-driven).
3. **Notification queued** — the detected change is placed in the MonitoredItem's notification queue.
4. **Publishing Interval tick** — the server checks all items in the subscription, batches any queued notifications, and sends a Publish Response to the client.

## Key Takeaway

The publishing interval is the **maximum additional latency** between a value change being detected and the client receiving the notification. It is a batching/polling timer, not a write delay.

## Configurable Properties

| Property                        | Side   | Description                                                                 |
|---------------------------------|--------|-----------------------------------------------------------------------------|
| `SubscriptionPublishingInterval`| Client | The interval (ms) the client *requests* from the server for the subscription. The server may revise it. |
| `MinPublishingInterval`         | Server | Lower bound enforced by the server. Client requests below this are clamped up. |
| `MaxPublishingInterval`         | Server | Upper bound enforced by the server. Client requests above this are clamped down. |

## Interaction Example

- Client requests `SubscriptionPublishingInterval = 500 ms`.
- Server has `MinPublishingInterval = 100 ms`, `MaxPublishingInterval = 1000 ms`.
- Server accepts 500 ms as-is (within range).

If the client requests 50 ms → server revises to 100 ms (min).  
If the client requests 2000 ms → server revises to 1000 ms (max).

## Verifying the Revised Interval

After creating the subscription, check `_subscription.CurrentPublishingInterval` on the client to see the actual value the server returned.
