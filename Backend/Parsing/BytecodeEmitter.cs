using System.ComponentModel;
using System.Text;
using T2G_Cookie.Shared.Ast;

namespace T2G_Cookie.Backend;

using Variables = Dictionary<string, int>;
using Bodies = Dictionary<int, BytecodeInstruction[]>;
public class BytecodeEmitter
{
    List<BytecodeInstruction> bytecodeInstructions;
    Bodies bodies;
    int count;
    public static BytecodeEmitter CreateEmitter(Body astRoot)
    {
        Variables variables = [];
        Bodies bodies = [];
        var body = ParseBody(astRoot, variables, bodies, -1);
        body.Add(new BytecodeInstruction
        {
            instructionKind = BytecodeInstruction.InstructionKind.End,
            metadata = BytecodeInstruction.MakeMetadata(-1, true)
        });

        BytecodeEmitter emitter = new BytecodeEmitter
        {
            bytecodeInstructions = body,
            count = variables.Count,
            bodies = bodies
        };

        return emitter;
    }

    static List<BytecodeInstruction> ParseBody(Body body, Variables variables, Bodies bodies, int layer)
    {
        if (body.Instructions.Length == 0) return [];

        List<BytecodeInstruction> bytecodeInstructions = new List<BytecodeInstruction>();

        foreach (var inst in body.Instructions)
        {
            int resumePc = bytecodeInstructions.Count + 1;
            bytecodeInstructions.AddRange(ParseInstruction(inst, variables, bodies, resumePc, layer));
        }

        return bytecodeInstructions;
    }


    static ValueUnion ParseValue(Value value, Variables variables)
    {
        if (value is Literal literal)
        {
            return new ValueUnion
            {
                type = ValueUnion.ValueType.Number,
                value = literal.value
            };
        }

        if (value is LiteralBool literalBool)
        {
            return new ValueUnion
            {
                type = ValueUnion.ValueType.Boolean,
                boolean = literalBool.value
            };
        }

        if (value is Variable variable)
        {
            int index;
            if (variables.ContainsKey(variable.variable))
            {
                index = variables[variable.variable];
            }
            else
            {
                index = variables.Count;
                variables[variable.variable] = index;
            }
            return new ValueUnion
            {
                type = ValueUnion.ValueType.Variable,
                variableId = index
            };
        }

        return new ValueUnion { };
    }

    // ez a drágaság azért tér vissza egy tömbbel is, mert a bytekódunk KIBASSZOTTUL egy parancsot tud per elem
    // ez a döntés akkor jött amikor megláttam hogy nyelvet kell írni (amit én nagyon szeretek csinálni)
    // és így nem olvastam végig teljesen a követelményeket, ezért az eredeti parser rekurzív volt,
    // de én már tudtam hogy bytecode kell, így úgy is találtam ki az opcode-okat hogy stack alapú legyen
    // de az ast még nestelve adja vissza, ézért lényegében pl a 2+2 először egy add 2 2 lesz
    // ami pushol a stackbe, majd az igazi érték egy NumberStack, amely azt mondja a VM-nek hogy 
    // ne kiszámolja, hanem nézze meg hogy mégis mi van a stackben és poppolja mint egy jófijú
    static (BytecodeInstruction[], ValueUnion) HandleValue(Value value, Variables variables, int line)
    {
        if (value is BinOp binOp)
        {
            var valueUnionLeft = ParseValue(binOp.left, variables);
            var valueUnionRight = ParseValue(binOp.right, variables);

            bool isBoolean = true;
            if (binOp.operation == BinOp.Operation.Add || binOp.operation == BinOp.Operation.Sub || binOp.operation == BinOp.Operation.Mul)
                isBoolean = false;

            return ([
                new BytecodeInstruction
                {
                    instructionKind = binOp.operation switch {
                        BinOp.Operation.Add => BytecodeInstruction.InstructionKind.Add,
                        BinOp.Operation.Sub => BytecodeInstruction.InstructionKind.Sub,
                        BinOp.Operation.Mul => BytecodeInstruction.InstructionKind.Mul,
                        BinOp.Operation.Eq =>  BytecodeInstruction.InstructionKind.Eq,
                        BinOp.Operation.Neq =>  BytecodeInstruction.InstructionKind.Neq,
                        BinOp.Operation.Gt =>  BytecodeInstruction.InstructionKind.Gt,
                        BinOp.Operation.Lt =>  BytecodeInstruction.InstructionKind.Lt,
                        BinOp.Operation.Leq =>  BytecodeInstruction.InstructionKind.Leq,
                        BinOp.Operation.Geq =>  BytecodeInstruction.InstructionKind.Geq,
                        BinOp.Operation.And =>  BytecodeInstruction.InstructionKind.And,
                        BinOp.Operation.Or =>  BytecodeInstruction.InstructionKind.Or,
                    },

                    instruction = new BytecodeInstruction.Instruction
                    {
                        binaryInstruction = new BytecodeInstruction.Instruction.BinInstruction
                        {
                            left = valueUnionLeft,
                            right = valueUnionRight
                        }
                    },
                    metadata = BytecodeInstruction.MakeMetadata(line, true)
                }
            ], new ValueUnion
            {
                type = isBoolean ? ValueUnion.ValueType.BooleanStack : ValueUnion.ValueType.NumberStack
            });
        }

        if (value is Not not)
        {
            (BytecodeInstruction[] instructions, ValueUnion valueUnion) = HandleValue(not.Value, variables, line);
            return
            (
               [
                    ..instructions,
                    new BytecodeInstruction
                    {
                        instructionKind = BytecodeInstruction.InstructionKind.Not,
                        instruction = new BytecodeInstruction.Instruction
                        {
                            singleValueInstruction = new BytecodeInstruction.Instruction.SingleValueInstruction
                            {
                                valueUnion = valueUnion
                            }
                        },
                        metadata = BytecodeInstruction.MakeMetadata(-1, true)
                    }
               ],
               new ValueUnion
               {
                   type = ValueUnion.ValueType.BooleanStack
               }
               
            );
        }

        if (value is IsThere isThere)
        {
            (BytecodeInstruction[] instructions, ValueUnion valueUnion) = HandleValue(isThere.Arg, variables, line);
            return
            (
               [
                    ..instructions,
                    new BytecodeInstruction
                    {
                        instructionKind = BytecodeInstruction.InstructionKind.Call,
                        instruction = new BytecodeInstruction.Instruction
                        {
                            singleValueInstruction = new BytecodeInstruction.Instruction.SingleValueInstruction
                            {
                                valueUnion = valueUnion
                            }
                        },
                        metadata = BytecodeInstruction.MakeMetadata(line, true)
                    }
               ],
               new ValueUnion
               {
                   type = ValueUnion.ValueType.BooleanStack
               }
               
            );
        }

        if (value is Literal || value is LiteralBool || value is Variable)
        {
            var union = ParseValue(value, variables);
            return ([], union);
        }

        return ([], new ValueUnion { });
    }

    static BytecodeInstruction[] ParseInstruction(Instruction instruction, Variables variables, Bodies bodies, int resumePc, int currentLayer)
    {
        switch (instruction)
        {
            case StepInstruction stepInstruction:
                {
                    (BytecodeInstruction[] bytecodes, ValueUnion union) = HandleValue(stepInstruction.Value, variables, stepInstruction.Line);
                    return [
                        .. bytecodes,
                        new BytecodeInstruction
                        {
                            instructionKind = BytecodeInstruction.InstructionKind.Step,
                            instruction = new BytecodeInstruction.Instruction
                            {
                                singleValueInstruction = new BytecodeInstruction.Instruction.SingleValueInstruction
                                {
                                    valueUnion = union
                                }
                            },
                            metadata = BytecodeInstruction.MakeMetadata(stepInstruction.Line, false)
                        }
                    ];
                }
            case TurnInstruction turnInstruction:
                {
                    (BytecodeInstruction[] bytecodes, ValueUnion union) = HandleValue(turnInstruction.Value, variables, turnInstruction.Line);
                    return [
                        .. bytecodes,
                        new BytecodeInstruction
                        {
                            instructionKind = BytecodeInstruction.InstructionKind.Turn,
                            instruction = new BytecodeInstruction.Instruction
                            {
                                singleValueInstruction = new BytecodeInstruction.Instruction.SingleValueInstruction
                                {
                                    valueUnion = union
                                }
                            },
                            metadata = BytecodeInstruction.MakeMetadata(turnInstruction.Line, false)
                        }
                    ];
                }
            case MineInstruction mineInstruction:
                {
                    return [
                        new BytecodeInstruction
                        {
                            instructionKind = BytecodeInstruction.InstructionKind.Mine,
                            metadata = BytecodeInstruction.MakeMetadata(mineInstruction.Line, false)
                        }
                    ];
                }
            case PrintInstruction printInstruction:
                {
                    (BytecodeInstruction[] bytecodes, ValueUnion union) = HandleValue(printInstruction.Value, variables, printInstruction.Line);
                    return [
                        ..bytecodes,
                        new BytecodeInstruction
                        {
                            instructionKind = BytecodeInstruction.InstructionKind.Print,
                            instruction = new BytecodeInstruction.Instruction
                            {
                                singleValueInstruction = new BytecodeInstruction.Instruction.SingleValueInstruction
                                {
                                    valueUnion = union
                                }
                            },
                            metadata = BytecodeInstruction.MakeMetadata(printInstruction.Line, false)
                        }
                    ];
                }
            case SetInstruction setInstruction:
                {
                    string var = setInstruction.variableName;
                    (BytecodeInstruction[] bytecodes, ValueUnion union) = HandleValue(setInstruction.Value, variables, setInstruction.Line);

                    if (!variables.ContainsKey(var))
                    {
                        int count = variables.Count;
                        variables[var] = count;
                    }

                    return
                    [
                        ..bytecodes,
                        new BytecodeInstruction
                        {
                            instructionKind = BytecodeInstruction.InstructionKind.Set,
                            instruction = new BytecodeInstruction.Instruction
                            {
                                setInstruction = new BytecodeInstruction.Instruction.SetInstruction
                                {
                                    variableId = variables[var],
                                    value = union
                                }
                            },
                            metadata = BytecodeInstruction.MakeMetadata(setInstruction.Line, false)
                        }
                    ];
                }
            case IfInstruction ifInstruction:
                {
                    (BytecodeInstruction[] conditionInstructions, ValueUnion valueUnion) = HandleValue(ifInstruction.Expression, variables, ifInstruction.Line);
                    int conditionId = bodies.Count;
                    int bodyId = bodies.Count + 1;
                    int elseBody = bodies.Count + 2;

                    bodies[conditionId] =
                    [
                        ..conditionInstructions,
                        BytecodeInstruction.Push(valueUnion),
                        BytecodeInstruction.If(bodyId, true, elseBody, ifInstruction.Line),
                    ];

                    bodies[bodyId] = [];
                    bodies[elseBody] = [];

                    var bodyInstructions = ParseBody(ifInstruction.body, variables, bodies, bodyId);
                    bodyInstructions.Add(BytecodeInstruction.Ret(currentLayer, resumePc));
                    bodies[bodyId] = bodyInstructions.ToArray();

                    var elseInstructions = ifInstruction.elseBody != null 
                        ? ParseBody(ifInstruction.elseBody, variables, bodies, elseBody) 
                        : [];
                    elseInstructions.Add(BytecodeInstruction.Ret(currentLayer, resumePc));
                    bodies[elseBody] = elseInstructions.ToArray();

                    return
                    [
                        BytecodeInstruction.Ret(conditionId, 0)
                    ];
                }

            // A while instruction az egy hazugság melyet a CIA talált fel
            // Nem is létezik bytecode-ban, 
            case WhileInstruction whileInstruction:
                {
                    (BytecodeInstruction[] conditionInstructions, ValueUnion valueUnion) = HandleValue(whileInstruction.Expression, variables, whileInstruction.Line);
                    int conditionId = bodies.Count;
                    int bodyId = bodies.Count + 1;
                    int exitId = bodies.Count + 2;

                    bodies[conditionId] = [];
                    bodies[bodyId] = [];
                    bodies[exitId] = [BytecodeInstruction.Ret(currentLayer, resumePc)];

                    bodies[conditionId] =
                    [
                        ..conditionInstructions,
                        BytecodeInstruction.Push(valueUnion),
                        BytecodeInstruction.If(bodyId, true, exitId, whileInstruction.Line, true),
                    ];

                    var bodyInstructions = ParseBody(whileInstruction.body, variables, bodies, bodyId);
                    bodyInstructions.Add(BytecodeInstruction.Ret(conditionId, 0));

                    bodies[bodyId] = bodyInstructions.ToArray();

                    return [
                        BytecodeInstruction.Ret(conditionId, 0)
                    ];
                }
        }
        throw new Exception();
    }

    public BytecodeHeader Generate()
    {

        return new BytecodeHeader
        {
            variableCount = count,
            Main = bytecodeInstructions.ToArray(),
            Bodies = bodies
        };
    }
}