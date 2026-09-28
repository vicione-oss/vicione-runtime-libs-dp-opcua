# DataPort OPC UA

| Provider                        | Incoming | Outgoing | free JSON structure |
|---------------------------------|:--------:|:--------:|:-------------------:|
| OPC UA Client                   |    ✔️    |    ✔️    |          ❌         |
| [OPC UA Server](#opc-ua-server) |    ✔️    |    ✔️    |          ❌         |

## Tests

Tests that stand up a real OPC UA server carry `[Trait("Category", "Interoperability")]` and are excluded from the
default pipeline, which runs the fast unit tests only. In CI they are the manual `dotnet Interoperability Test` job,
available on merge request, default branch and web pipelines. Locally:

```sh
dotnet test dp-opcua.slnx -- --filter-query "/[Category=Interoperability]" --ignore-exit-code 8
```

## OPC UA Server

### Node properties

| Property name | Node type                   | Usage                                                                                                                                                                                                                                                     |
|---------------|-----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Maximum       | Variable                    | Defines the maximum value a client may send before a `BadOutOfRange` status is returned. Not supported for nodes of type `string`.                                                                                                                        |
| Minimum       | Variable                    | Defines the minimum value a client may send before a `BadOutOfRange` status is returned. Not supported for nodes of type `string`.                                                                                                                        |

Neither property is declared in `OpcUaServer.yaml`, so the configuration editor cannot offer them
and a configuration has to set them by hand. That is the position the `Status` property sat in until
it became the `Status code` envelope child, which the ruleset does declare.

## Envelope children

A data point may carry child nodes that address the OPC UA value of their parent rather than a node
of their own. They appear in the tree below the data point, they have no counterpart in the address
space, and they transfer nothing themselves — their value travels with the value of the parent.

Each child names exactly one field of the OPC UA `DataValue`, so it never has to say which of
several it happens to be carrying. Which field that is, is decided by the child the configuration
picked, never by what it was called — the name is a label for whoever reads the tree, and two data
points are free to name theirs differently.

| Child            | Server                                                                                                      | Client                                                                                                            |
|------------------|-------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------|
| Status code      | **Out:** sets the status the variable is served with. **In:** forwards the status a client wrote.             | **Out:** writes the value with that status instead of `Good`. **In:** forwards the status the server sent.            |
| Source timestamp | **Out:** sets the time the variable reports its value for. **In:** forwards the time a client wrote for.      | **Out:** writes the value for that time instead of letting the server stamp it. **In:** forwards the time the server sent. |
| Server timestamp | *Not offered* — a server sets its own and refuses one a client writes.                                       | **In only:** forwards the time the server says it processed the value.                                               |

Each child is offered at most once per data point, and only in the directions its row names — the
editor offers no connector in any other. A child the configuration links nothing to exchanges no
value.

The children exist so the dataflow can **compute** with the envelope. A received value already
carries its timestamp as metadata; the child makes it a value on a channel of its own, which a
dataflow can compare, store or forward.

A child says what the far side said about one value. It says nothing about whether the connection is
up — a data point goes on reporting the last status it was given while the link is down, and the
value a dataflow reads is marked valid either way. Signalling a lost connection is a separate
concern and is not part of this.

### A read-only data point takes nothing in

`Read only` stops a server variable from accepting a write at all, so no value, status or timestamp
ever arrives on it and its inbound children are connectors that can never carry anything. Leave the
property off a data point whose envelope is meant to be received.

### A cycle that carries only children

The engine sends a channel only in the cycle its value changes in, so a cycle may bring a status
code or a timestamp with no value beside it. The client writes nothing at all in that cycle,
because OPC UA writes a status and a timestamp only together with a value. The server applies a
status code to the variable straight away, because the variable is already holding the value it
belongs to.

A status code is a state that holds until it changes, so both ports remember it and serve it with
every later value of its parent. A source timestamp belongs to the one value it arrives with and is
not remembered: the engine does not send a timestamp again that did not change, so carrying it over
would stamp a later value with the time of an earlier one. A value that arrives without a source
timestamp in its cycle falls back to the default below, and a timestamp that arrives without its
value is dropped.

### What a child carries when there is nothing to carry

A timestamp child forwards only what the far side actually sent. A server that sends no source
timestamp, or a client that writes without one, leaves that child silent for the cycle rather than
reporting a substitute. The value itself is always timestamped: inbound on the client it falls back
from source to server timestamp and finally to the time the notification was published, and inbound
on the server a write without a timestamp is stamped with the moment it arrived.

### Defaults an outbound link overwrites

| Linked outbound  | Default when nothing arrives                                               |
|------------------|----------------------------------------------------------------------------|
| Status code      | `Good` (client); `BadWaitingForInitialData` until the first value and for a value that is not a number, `Good` otherwise (server) |
| Source timestamp | the timestamp the engine gave the value (server), unset so that the receiving server stamps it (client) |

A linked status code replaces those defaults: the server serves a variable exactly as the tree says,
also on a value that is not a number.

A status code is linked either as a `UInt32`, which is what OPC UA transmits, or as a `String`
holding the name of the code, such as `BadNotFound`. Names are case-sensitive and read with or
without the underscore, so `BadEdited_OutOfRange` and `BadEditedOutOfRange` both resolve; a
`String` may also hold the number of the code, in decimal or as hexadecimal with a `0x` prefix. A
status code the port cannot read is written or served as `BadInternalError` and reported with a
warning. A list of status codes is available here: <https://reference.opcfoundation.org/Core/Part6/v104/docs/A.2>

Inbound, a `String` child carries the name of the code without its info bits, such as the limit bits
of a value clamped at its high limit, and a code the stack has no name for as its hexadecimal
number. Link a `UInt32` to keep every bit when the status travels on to another port.

A writable data point advertises `StatusWrite` and `TimestampWrite` in its access level, which is
what a client reads to decide whether to offer those children at all. A foreign server that refuses
a written status or timestamp refuses the value with it; the refusal is logged as an error naming
the node and what was written with the value.

A **server timestamp** is never writable — the OPC UA stack rejects any write that carries one with
`BadWriteNotSupported`, and the value does not arrive either. That is why the child exists only on
the client, and only inbound: a server has no way to accept one and no field to set one on the
values it serves.
