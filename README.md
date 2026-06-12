# DataPort OPC UA

| Provider                        | Incoming | Outgoing | free JSON structure |
|---------------------------------|:--------:|:--------:|:-------------------:|
| OPC UA Client                   |    ✔️    |    ✔️    |          ❌         |
| [OPC UA Server](#opc-ua-server) |    ✔️    |    ✔️    |          ❌         |

## OPC UA Server

### Node properties

| Property name | Node type                   | Usage                                                                                                                                                                                                                                                     |
|---------------|-----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Maximum       | Variable                    | Defines the maximum value a client may send before a `BadOutOfRange` status is returned. Not supported for nodes of type `string`.                                                                                                                        |
| Minimum       | Variable                    | Defines the minimum value a client may send before a `BadOutOfRange` status is returned. Not supported for nodes of type `string`.                                                                                                                        |
| Status        | Variable, Writable Variable | Intended to set node status values at runtime. You can pass either status code names as `string` values or the numeric status code as `uint`. A list of status codes is available here: <https://reference.opcfoundation.org/specs/OPC-10000-6/v1.04/a-2> |
