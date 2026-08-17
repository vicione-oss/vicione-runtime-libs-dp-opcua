# DataPort OPC UA

| Provider                        | Incoming | Outgoing | free JSON structure |
|---------------------------------|:--------:|:--------:|:-------------------:|
| OPC UA Client                   |    ✔️    |    ✔️    |          ❌         |
| [OPC UA Server](#opc-ua-server) |    ✔️    |    ✔️    |          ❌         |

## Tests

Tests that stand up a real OPC UA server carry `[Trait("Category", "Interoperability")]` and are excluded from the
default pipeline, which runs the fast unit tests only. Run them on demand:

```sh
dotnet test dp-opcua.slnx -- --filter-query "/[Category=Interoperability]" --ignore-exit-code 8
```

## OPC UA Server

### Node properties

| Property name | Node type                   | Usage                                                                                                                                                                                                                                                     |
|---------------|-----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Maximum       | Variable                    | Defines the maximum value a client may send before a `BadOutOfRange` status is returned. Not supported for nodes of type `string`.                                                                                                                        |
| Minimum       | Variable                    | Defines the minimum value a client may send before a `BadOutOfRange` status is returned. Not supported for nodes of type `string`.                                                                                                                        |
| Status        | Variable, Writable Variable | Intended to set node status values at runtime. You can pass either status code names as `string` values or the numeric status code as `uint`. A list of status codes is available here: <https://reference.opcfoundation.org/specs/OPC-10000-6/v1.04/a-2> |
