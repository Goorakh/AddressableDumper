﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.Utils;
using System;
using System.Collections;
using System.Collections.Generic;

namespace AddressableDumper.Utils.Extensions
{
    public static class PatchExtensions
    {
        public static void EmitSkipMethodCall(this ILCursor c, MethodDefinition method = null)
        {
            EmitSkipMethodCall(c, OpCodes.Br, method);
        }

        public static void EmitSkipMethodCall(this ILCursor c, OpCode branchOpCode, MethodDefinition method = null)
        {
            EmitSkipMethodCall(c, branchOpCode, null, method);
        }

        public static void EmitSkipMethodCall(this ILCursor c, OpCode branchOpCode, Action<ILCursor> emitSkippedReturnValue, MethodDefinition method = null)
        {
            if (c is null)
                throw new ArgumentNullException(nameof(c));

            if (branchOpCode.FlowControl != FlowControl.Branch && branchOpCode.FlowControl != FlowControl.Cond_Branch)
                throw new ArgumentException($"Invalid branch OpCode: {branchOpCode}");

            if (method == null)
            {
                if (!c.Next.MatchCallOrCallvirt(out MethodReference nextMethodCall))
                {
                    Log.Error($"Failed to find method call to skip: {c.Context.Method.FullName} at instruction {c.Next} ({c.Index})");
                    return;
                }

                method = nextMethodCall.SafeResolve();

                if (method == null)
                {
                    Log.Error($"Failed to resolve method '{nextMethodCall.FullName}': {c.Context.Method.FullName} at instruction {c.Next} ({c.Index})");
                    return;
                }
            }

            int parameterCount = method.Parameters.Count + (!method.IsStatic ? 1 : 0);
            bool isVoidReturn = method.ReturnType.Is(typeof(void));

            ILLabel skipCallLabel = c.DefineLabel();

            c.Emit(branchOpCode, skipCallLabel);

            c.Index++;

            if (parameterCount > 0 || !isVoidReturn)
            {
                ILLabel afterPatchLabel = c.DefineLabel();
                c.Emit(OpCodes.Br, afterPatchLabel);

                c.MarkLabel(skipCallLabel);

                for (int i = 0; i < parameterCount; i++)
                {
                    c.Emit(OpCodes.Pop);
                }

                if (emitSkippedReturnValue != null)
                {
                    emitSkippedReturnValue(c);
                }
                else if (!isVoidReturn)
                {
                    Log.Warning($"Skipped method ({method.FullName}) is not void, emitting default value: {c.Context.Method.FullName} at instruction {c.Next} ({c.Index})");

                    if (method.ReturnType.IsValueType)
                    {
                        VariableDefinition tmpVar = c.Context.AddVariable(method.ReturnType);

                        c.Emit(OpCodes.Ldloca, tmpVar);
                        c.Emit(OpCodes.Initobj, method.ReturnType);

                        c.Emit(OpCodes.Ldloc, tmpVar);
                    }
                    else
                    {
                        c.Emit(OpCodes.Ldnull);
                    }
                }

                c.MarkLabel(afterPatchLabel);
            }
            else
            {
                c.MarkLabel(skipCallLabel);
            }
        }

        public static bool TryFindForeachContinueLabel(this ILCursor cursor, out ILLabel continueLabel)
        {
            static bool isEnumerableGetEnumerator(MethodReference method)
            {
                if (method == null)
                    return false;

                if (!string.Equals(method.Name, nameof(IEnumerable.GetEnumerator)))
                    return false;

                // TODO: More robust check here, check return type is an IEnumerator and make sure declaring type is actually implementing the IEnumerable interface

                return true;
            }

            ILCursor c = cursor.Clone();

            int enumeratorLocalIndex = -1;
            if (!c.TryGotoPrev(x => x.MatchCallOrCallvirt(out MethodReference method) && isEnumerableGetEnumerator(method)) ||
                !c.TryGotoNext(x => x.MatchStloc(out enumeratorLocalIndex)))
            {
                Log.Warning("Failed to find GetEnumerator call");
                continueLabel = null;
                return false;
            }

            c = cursor.Clone();
            if (!c.TryGotoNext(x => x.MatchCallOrCallvirt<IEnumerator>(nameof(IEnumerator.MoveNext))) ||
                !c.TryGotoPrev(MoveType.Before, x => x.MatchLdloc(enumeratorLocalIndex)))
            {
                Log.Warning("Failed to find matching MoveNext call");
                continueLabel = null;
                return false;
            }

            continueLabel = c.MarkLabel();
            return true;
        }

        public static bool TryFindParameter(this MethodReference method, Type type, string name, out ParameterDefinition parameter)
        {
            if (type == null && string.IsNullOrEmpty(name))
            {
                Log.Error($"Cannot find parameter for method {method.FullName}: Neither parameter type or name specified");
                parameter = null;
                return false;
            }

            foreach (ParameterDefinition param in method.Parameters)
            {
                if ((string.IsNullOrEmpty(name) || param.Name == name) && (type == null || param.ParameterType.Is(type)))
                {
                    parameter = param;
                    return true;
                }
            }

            parameter = null;
            return false;
        }

        public static bool TryFindParameter(this MethodReference method, string name, out ParameterDefinition parameter)
        {
            return TryFindParameter(method, null, name, out parameter);
        }

        public static bool TryFindParameter(this MethodReference method, Type type, out ParameterDefinition parameter)
        {
            return TryFindParameter(method, type, null, out parameter);
        }

        public static bool TryFindParameter<T>(this MethodReference method, string name, out ParameterDefinition parameter)
        {
            return TryFindParameter(method, typeof(T), name, out parameter);
        }

        public static bool TryFindParameter<T>(this MethodReference method, out ParameterDefinition parameter)
        {
            return TryFindParameter(method, typeof(T), null, out parameter);
        }

        public static VariableDefinition AddVariable(this ILContext context, TypeReference variableType)
        {
            VariableDefinition variableDefinition = new VariableDefinition(variableType);
            context.Method.Body.Variables.Add(variableDefinition);
            return variableDefinition;
        }

        public static VariableDefinition AddVariable(this ILContext context, Type variableType)
        {
            return AddVariable(context, context.Import(variableType));
        }

        public static VariableDefinition AddVariable<T>(this ILContext context)
        {
            return AddVariable(context, context.Import(typeof(T)));
        }

        /// <summary>
        /// Stores all values on the stack in the variables represented by the <paramref name="variables"/> parameter
        /// </summary>
        /// <param name="cursor"></param>
        /// <param name="variables">The variables to store the stack's values in, defined in the order the values should be pushed onto the stack</param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void EmitStoreStack(this ILCursor cursor, params IReadOnlyList<VariableDefinition> variables)
        {
            if (cursor is null)
                throw new ArgumentNullException(nameof(cursor));

            if (variables is null)
                throw new ArgumentNullException(nameof(variables));

            if (variables.Count == 0)
                return;

            for (int i = variables.Count - 1; i >= 1; i--)
            {
                cursor.Emit(OpCodes.Stloc, variables[i]);
            }

            cursor.Emit(OpCodes.Dup);
            cursor.Emit(OpCodes.Stloc, variables[0]);

            for (int i = 1; i < variables.Count; i++)
            {
                cursor.Emit(OpCodes.Ldloc, variables[i]);
            }
        }
    }
}
