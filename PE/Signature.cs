//
// Signatures (S23.2) and custom attributes (S23.3)
//

using System;
using System.Linq;
using Microsoft.LiveLabs.Extras;

namespace Microsoft.LiveLabs.PE
{
    public abstract class Signature
    {
        public abstract void ReadRest(ReaderContext ctxt, BlobReader reader);
        public abstract void ResolveIndexes(ReaderContext ctxt);
        public abstract void PersistIndexes(WriterContext ctxt);
        public abstract void Write(WriterContext ctxt, BlobWriter writer);
    }

    // ----------------------------------------------------------------------
    // Type signatures
    // ----------------------------------------------------------------------

    public enum TypeSigFlavor {
        PinnedPsuedo,
        SentinelPsuedo,
        Primitive,
        UnmanagedPointer,
        ManagedPointer,
        Array,
        MultiDimArray,
        TypeDefOrRef,
        CustomModPsuedo,
        TypeParameter,
        Application,
        MethodParameter,
        FunctionPointer
    }

    // S23.2.12, S23.2.14
    public abstract class TypeSig : Signature
    {
        public abstract TypeSigTag Tag { get; }
        public abstract TypeSigFlavor Flavor { get; }

        public static TypeSig Read(ReaderContext ctxt, BlobReader reader)
        {
            var tag = (TypeSigTag)reader.ReadCompressedUInt32();
            var res = default(TypeSig);
            switch (tag)
            {
                case TypeSigTag.END:
                    return null;
                case TypeSigTag.SENTINEL:
                    res = new SentinelPsuedoTypeSig();
                    break;
                case TypeSigTag.PINNED:
                    res = new PinnedPsuedoTypeSig();
                    break;
                case TypeSigTag.VOID:
                case TypeSigTag.BOOLEAN:
                case TypeSigTag.CHAR:
                case TypeSigTag.I1:
                case TypeSigTag.U1:
                case TypeSigTag.I2:
                case TypeSigTag.U2:
                case TypeSigTag.I4:
                case TypeSigTag.U4:
                case TypeSigTag.I8:
                case TypeSigTag.U8:
                case TypeSigTag.R4:
                case TypeSigTag.R8:
                case TypeSigTag.STRING:
                case TypeSigTag.I:
                case TypeSigTag.U:
                case TypeSigTag.OBJECT:
                case TypeSigTag.SYSTYPE_ARGUMENT: // NOTE: Not mentioned in spec...
                case TypeSigTag.TYPEDBYREF:
                    res = new PrimitiveTypeSig { PrimitiveType = PrimitiveTypeSig.FromTag(tag) };
                    break;
                case TypeSigTag.PTR:
                    res = new UnmanagedPointerTypeSig();
                    break;
                case TypeSigTag.BYREF:
                    res = new ManagedPointerTypeSig();
                    break;
                case TypeSigTag.SZARRAY:
                    res = new ArrayTypeSig();
                    break;
                case TypeSigTag.ARRAY:
                    res = new MultiDimArrayTypeSig();
                    break;
                case TypeSigTag.VALUETYPE:
                case TypeSigTag.CLASS:
                    res = new TypeDefOrRefSig { IsValueType = tag == TypeSigTag.VALUETYPE };
                    break;
                case TypeSigTag.CMOD_REQD:
                case TypeSigTag.CMOD_OPT:
                    res = new CustomModPseudoTypeSig { IsRequired = tag == TypeSigTag.CMOD_REQD };
                    break;
                case TypeSigTag.VAR:
                    res = new TypeParameterTypeSig();
                    break;
                case TypeSigTag.GENERICINST:
                    res = new ApplicationTypeSig();
                    break;
                case TypeSigTag.MVAR:
                    res = new MethodParameterTypeSig();
                    break;
                case TypeSigTag.FNPTR:
                    res = new FunctionPointerTypeSig();
                    break;
                case TypeSigTag.INTERNAL:
                case TypeSigTag.RESERVED:
                    throw new PEException("unimplemented type tag");
                case TypeSigTag.CUSTOM_ATTRIBUTE_BOXED_ARGUMENT:
                case TypeSigTag.CUSTOM_ATTRIBUTE_FIELD:
                case TypeSigTag.CUSTOM_ATTRIBUTE_PROPERTY:
                case TypeSigTag.CUSTOM_ATTRIBUTE_ENUM:
                    throw new PEException("unexpected custom attribute in type");
                default:
                    throw new PEException("unrecognised type tag");
            }

            res.ReadRest(ctxt, reader);
            return res;
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            writer.WriteCompressedUInt32((uint)Tag);
        }

        public abstract uint ByteSize(MetadataTables tables);

        public virtual bool IsCustomMod
        {
            get { return false; }
        }

        public virtual bool IsPinned
        {
            get { return false; }
        }

        public virtual bool IsSentinel
        {
            get { return false; }
        }
    }

    // S23.2.10, S23.2.11, also factored out of S23.2.12, S23.2.14
    public class TypeWithCustomMods
    {
        public IImSeq<CustomModPseudoTypeSig> CustomMods;
        public TypeSig Type;

        public void Read(ReaderContext ctxt, BlobReader reader)
        {
            var customMods = default(Seq<CustomModPseudoTypeSig>);
            var type = TypeSig.Read(ctxt, reader);
            while (type.IsCustomMod)
            {
                if (customMods == null)
                    customMods = new Seq<CustomModPseudoTypeSig>();
                customMods.Add((CustomModPseudoTypeSig)type);
                type = TypeSig.Read(ctxt, reader);
            }
            CustomMods = customMods ?? Constants.EmptyCustomModSigs;
            Type = type;
        }

        public void ResolveIndexes(ReaderContext ctxt)
        {
            foreach (var type in CustomMods)
                type.ResolveIndexes(ctxt);
            Type.ResolveIndexes(ctxt);
        }

        public void PersistIndexes(WriterContext ctxt)
        {
            foreach (var type in CustomMods)
                type.PersistIndexes(ctxt);
            Type.PersistIndexes(ctxt);
        }

        public void Write(WriterContext ctxt, BlobWriter writer)
        {
            foreach (var type in CustomMods)
                type.Write(ctxt, writer);
            Type.Write(ctxt, writer);
        }
    }

    // S23.2.9
    public class PinnedPsuedoTypeSig : TypeSig
    {
        public override TypeSigTag Tag
        {
            get { return TypeSigTag.PINNED; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.PinnedPsuedo; } }

        public override bool IsPinned
        {
            get { return true; }
        }

        public override void ReadRest(ReaderContext ctxt, BlobReader reader) { }
        public override void ResolveIndexes(ReaderContext ctxt) { }
        public override void PersistIndexes(WriterContext ctxt) { }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new PEException("psueudo types do not have sizes");
        }
    }

    // Factored out of S23.2.2, S23.2.3
    public class SentinelPsuedoTypeSig : TypeSig
    {
        public override TypeSigTag Tag
        {
            get
            {
                return TypeSigTag.SENTINEL;
            }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.SentinelPsuedo; } }

        public override bool IsSentinel
        {
            get { return true; }
        }

        public override void ReadRest(ReaderContext ctxt, BlobReader reader) { }
        public override void ResolveIndexes(ReaderContext ctxt) { }
        public override void PersistIndexes(WriterContext ctxt) { }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new PEException("psueudo types do not have sizes");
        }
    }

    public enum PrimitiveType
    {
        Boolean,
        Char,
        Int8,
        Int16,
        Int32,
        Int64,
        IntNative,
        UInt8,
        UInt16,
        UInt32,
        UInt64,
        UIntNative,
        Single,
        Double,
        Object,
        String,
        TypedRef,
        Type,
        Void
    }

    public class PrimitiveTypeSig : TypeSig
    {
        public PrimitiveType PrimitiveType;

        public static PrimitiveType FromTag(TypeSigTag tag)
        {
            switch (tag)
            {
                case TypeSigTag.VOID:
                    return PrimitiveType.Void;
                case TypeSigTag.BOOLEAN:
                    return PrimitiveType.Boolean;
                case TypeSigTag.CHAR:
                    return PrimitiveType.Char;
                case TypeSigTag.I1:
                    return PrimitiveType.Int8;
                case TypeSigTag.U1:
                    return PrimitiveType.UInt8;
                case TypeSigTag.I2:
                    return PrimitiveType.Int16;
                case TypeSigTag.U2:
                    return PrimitiveType.UInt16;
                case TypeSigTag.I4:
                    return PrimitiveType.Int32;
                case TypeSigTag.U4:
                    return PrimitiveType.UInt32;
                case TypeSigTag.I8:
                    return PrimitiveType.Int64;
                case TypeSigTag.U8:
                    return PrimitiveType.UInt64;
                case TypeSigTag.R4:
                    return PrimitiveType.Single;
                case TypeSigTag.R8:
                    return PrimitiveType.Double;
                case TypeSigTag.STRING:
                    return PrimitiveType.String;
                case TypeSigTag.TYPEDBYREF:
                    return PrimitiveType.TypedRef;
                case TypeSigTag.I:
                    return PrimitiveType.IntNative;
                case TypeSigTag.U:
                    return PrimitiveType.UIntNative;
                case TypeSigTag.OBJECT:
                    return PrimitiveType.Object;
                case TypeSigTag.SYSTYPE_ARGUMENT:
                    return PrimitiveType.Type;
                default:
                    throw new ArgumentOutOfRangeException("tag");
            }
        }

        public static TypeSigTag ToTag(PrimitiveType type)
        {
            switch (type)
            {
                case PrimitiveType.Boolean:
                    return TypeSigTag.BOOLEAN;
                case PrimitiveType.Char:
                    return TypeSigTag.CHAR;
                case PrimitiveType.Int8:
                    return TypeSigTag.I1;
                case PrimitiveType.Int16:
                    return TypeSigTag.I2;
                case PrimitiveType.Int32:
                    return TypeSigTag.I4;
                case PrimitiveType.Int64:
                    return TypeSigTag.I8;
                case PrimitiveType.UInt8:
                    return TypeSigTag.U1;
                case PrimitiveType.UInt16:
                    return TypeSigTag.U2;
                case PrimitiveType.UInt32:
                    return TypeSigTag.U4;
                case PrimitiveType.UInt64:
                    return TypeSigTag.U8;
                case PrimitiveType.IntNative:
                    return TypeSigTag.I;
                case PrimitiveType.UIntNative:
                    return TypeSigTag.U;
                case PrimitiveType.Single:
                    return TypeSigTag.R4;
                case PrimitiveType.Double:
                    return TypeSigTag.R8;
                case PrimitiveType.Object:
                    return TypeSigTag.OBJECT;
                case PrimitiveType.String:
                    return TypeSigTag.STRING;
                case PrimitiveType.TypedRef:
                    return TypeSigTag.TYPEDBYREF;
                case PrimitiveType.Type:
                    return TypeSigTag.SYSTYPE_ARGUMENT;
                case PrimitiveType.Void:
                    return TypeSigTag.VOID;
                default:
                    throw new ArgumentOutOfRangeException("type");
            }
        }

        public override TypeSigTag Tag
        {
            get { return ToTag(PrimitiveType); }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.Primitive; } }

        public override uint ByteSize(MetadataTables tables)
        {
            switch (PrimitiveType)
            {
                case PrimitiveType.Boolean:
                case PrimitiveType.Int8:
                case PrimitiveType.UInt8:
                    return 1;
                case PrimitiveType.Char:
                case PrimitiveType.Int16:
                case PrimitiveType.UInt16:
                    return 2;
                case PrimitiveType.Int32:
                case PrimitiveType.UInt32:
                case PrimitiveType.Single:
                    return 4;
                case PrimitiveType.Int64:
                case PrimitiveType.UInt64:
                case PrimitiveType.Double:
                    return 8;
                case PrimitiveType.Void:
                    throw new PEException("void type does not have a size");
                case PrimitiveType.IntNative:
                case PrimitiveType.UIntNative:
                case PrimitiveType.String:
                case PrimitiveType.TypedRef:
                case PrimitiveType.Type:
                case PrimitiveType.Object:
                    throw new PEException("cannot determine size of type");
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public override void ReadRest(ReaderContext ctxt, BlobReader reader) { }
        public override void ResolveIndexes(ReaderContext ctxt) { }
        public override void PersistIndexes(WriterContext ctxt) { }
    }

    public class UnmanagedPointerTypeSig : TypeSig
    {
        public TypeWithCustomMods ElementType;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            ElementType = new TypeWithCustomMods();
            ElementType.Read(ctxt, reader);
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.PTR; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.UnmanagedPointer; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            ElementType.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            ElementType.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            ElementType.Write(ctxt, writer);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new NotImplementedException();
        }
    }

    public class ManagedPointerTypeSig : TypeSig
    {
        public TypeSig ElementType;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            ElementType = Read(ctxt, reader);
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.BYREF; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.ManagedPointer; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            ElementType.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            ElementType.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            ElementType.Write(ctxt, writer);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new NotImplementedException();
        }
    }

    public class ArrayTypeSig : TypeSig
    {
        public TypeWithCustomMods ElementType;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            ElementType = new TypeWithCustomMods();
            ElementType.Read(ctxt, reader);
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.SZARRAY; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.Array; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            ElementType.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            ElementType.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            ElementType.Write(ctxt, writer);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new NotImplementedException();
        }
    }

    // S23.2.13
    public class MultiDimArrayTypeSig : TypeSig
    {
        public TypeSig ElementType;
        public int Rank;
        public IImSeq<uint> Sizes;
        public IImSeq<uint> LoBounds;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            ElementType = Read(ctxt, reader);
            Rank = (int)reader.ReadCompressedUInt32();
            var numSizes = (int)reader.ReadCompressedUInt32();
            var sizes = new Seq<uint>(numSizes);
            for (var i = 0; i < numSizes; i++)
                sizes.Add(reader.ReadCompressedUInt32());
            Sizes = sizes;
            var numLoBounds = (int)reader.ReadCompressedUInt32();
            var loBounds = new Seq<uint>(numLoBounds);
            for (var i = 0; i < numLoBounds; i++)
                loBounds.Add(reader.ReadCompressedUInt32());
            LoBounds = loBounds;
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.ARRAY; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.MultiDimArray; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            ElementType.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            ElementType.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            ElementType.Write(ctxt, writer);
            writer.WriteCompressedUInt32((uint)Rank);
            writer.WriteCompressedUInt32((uint)Sizes.Count);
            foreach (var size in Sizes)
                writer.WriteCompressedUInt32(size);
            writer.WriteCompressedUInt32((uint)LoBounds.Count);
            foreach (var loBound in LoBounds)
                writer.WriteCompressedUInt32(loBound);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new NotImplementedException();
        }
    }

    public class TypeDefOrRefSig : TypeSig
    {
        public bool IsValueType;
        public TypeDefOrRefVarLenRef TypeDefOrRef;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            TypeDefOrRef.Read(ctxt, reader);
        }

        public override TypeSigTag Tag
        {
            get { return IsValueType ? TypeSigTag.VALUETYPE : TypeSigTag.CLASS; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.TypeDefOrRef; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            TypeDefOrRef.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            TypeDefOrRef.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            TypeDefOrRef.Write(ctxt, writer);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            var typeDefRow = TypeDefOrRef.Value as TypeDefRow;
            if (typeDefRow != null)
            {
                var classLayoutRow = tables.ClassLayoutTable.SingleOrDefault(cl => cl.Parent.Value == typeDefRow);
                if (classLayoutRow != null)
                    return classLayoutRow.ClassSize;
            }

            if (!IsValueType)
                return 0;

            throw new PEException("cannot determine size of type");
        }
    }

    public class CustomModPseudoTypeSig : TypeSig
    {
        public bool IsRequired;
        public TypeDefOrRefVarLenRef TypeDefOrRef;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            TypeDefOrRef.Read(ctxt, reader);
        }

        public override TypeSigTag Tag
        {
            get { return IsRequired ? TypeSigTag.CMOD_REQD : TypeSigTag.CMOD_OPT; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.CustomModPsuedo; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            TypeDefOrRef.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            TypeDefOrRef.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            TypeDefOrRef.Write(ctxt, writer);
        }

        public override bool IsCustomMod
        {
            get { return true; }
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new PEException("pseudo types do not have sizes");
        }
    }

    public class TypeParameterTypeSig : TypeSig
    {
        public int Index;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            Index = (int)reader.ReadCompressedUInt32();
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.VAR; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.TypeParameter; } }

        public override void ResolveIndexes(ReaderContext ctxt) { }
        public override void PersistIndexes(WriterContext ctxt) { }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            writer.WriteCompressedUInt32((uint)Index);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new PEException("type parameters do not have sizes");
        }
    }

    public class ApplicationTypeSig : TypeSig
    {
        public TypeSig Applicand;
        public IImSeq<TypeSig> Arguments;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            Applicand = Read(ctxt, reader);
            var argCount = (int)reader.ReadCompressedUInt32();
            var arguments = new Seq<TypeSig>(argCount);
            for (var i = 0; i < argCount; i++)
                arguments.Add(Read(ctxt, reader));
            Arguments = arguments;
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.GENERICINST; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.Application; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            Applicand.ResolveIndexes(ctxt);
            foreach (var argument in Arguments)
                argument.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            Applicand.PersistIndexes(ctxt);
            foreach (var argument in Arguments)
                argument.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            Applicand.Write(ctxt, writer);
            writer.WriteCompressedUInt32((uint)Arguments.Count);
            foreach (var argument in Arguments)
                argument.Write(ctxt, writer);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new NotImplementedException();
        }
    }

    public class MethodParameterTypeSig : TypeSig
    {
        public int Index;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            Index = (int)reader.ReadCompressedUInt32();
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.MVAR; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.MethodParameter; } }

        public override void ResolveIndexes(ReaderContext ctxt) { }
        public override void PersistIndexes(WriterContext ctxt) { }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            writer.WriteCompressedUInt32((uint)Index);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new PEException("method type parameters do not have sizes");
        }
    }

    public class FunctionPointerTypeSig : TypeSig
    {
        public MethodMemberSig Method;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            Method = MethodMemberSig.ReadMethod(ctxt, reader);
        }

        public override TypeSigTag Tag
        {
            get { return TypeSigTag.FNPTR; }
        }

        public override TypeSigFlavor Flavor { get { return TypeSigFlavor.FunctionPointer; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            Method.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            Method.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            Method.Write(ctxt, writer);
        }

        public override uint ByteSize(MetadataTables tables)
        {
            throw new NotImplementedException();
        }
    }

    // ----------------------------------------------------------------------
    // Member signatures
    // ----------------------------------------------------------------------

    public enum MemberSigFlavor {
        Method,
        MethodSpec,
        Field,
        Property,
        LocalVar
    }

    public abstract class MemberSig : Signature
    {
        public static MemberSig Read(ReaderContext ctxt, BlobReader reader)
        {
            var tag = (MemberSigTag)reader.ReadByte();
            var res = default(MemberSig);
            switch (tag & MemberSigTag.MASK)
            {
                case MemberSigTag.FIELD:
                    res = new FieldMemberSig();
                    break;
                case MemberSigTag.PROPERTY:
                    res = new PropertyMemberSig { Tag = tag };
                    break;
                case MemberSigTag.LOCAL_SIG:
                    res = new LocalVarMemberSig();
                    break;
                case MemberSigTag.GENERICINST:
                    res = new MethodSpecMemberSig();
                    break;
                default:
                    res = new MethodMemberSig { Tag = tag };
                    break;
            }
            res.ReadRest(ctxt, reader);
            return res;
        }

        public abstract MemberSigTag Tag { get; set; }

        public abstract MemberSigFlavor Flavor { get; }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            writer.WriteByte((byte)Tag);
        }
    }

    public enum CallingConvention
    {
        Managed,
        ManagedVarArg,
        NativeC,
        NativeStd,
        NativeThis,
        NativeFast
    }

    // S23.2.1, S23.2.2, S23.2.3
    public class MethodMemberSig : MemberSig
    {
        public bool IsStatic;
        public bool IsExplicitThis;
        public CallingConvention CallingConvention;
        public int TypeArity;
        public TypeWithCustomMods ReturnType;
        public IImSeq<TypeWithCustomMods> Parameters;
        // Number of var-arg parameters (at end of parameters list)
        public int VarArgs;

        public static MethodMemberSig ReadMethod(ReaderContext ctxt, BlobReader reader)
        {
            var tag = (MemberSigTag)reader.ReadByte();
            var res = new MethodMemberSig { Tag = tag };
            res.ReadRest(ctxt, reader);
            return res;
        }

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            if (TypeArity > 0)
                TypeArity = (int)reader.ReadCompressedUInt32();

            var paramCount = (int)reader.ReadCompressedUInt32();
            ReturnType = new TypeWithCustomMods();
            ReturnType.Read(ctxt, reader);
            var sentinelIndex = -1;
            if (paramCount > 0)
            {
                var parameters = new Seq<TypeWithCustomMods>(paramCount);
                for (var i = 0; i < paramCount; i++)
                {
                    var param = new TypeWithCustomMods();
                    param.Read(ctxt, reader);
                    if (param.Type.IsSentinel)
                    {
                        if (CallingConvention == CallingConvention.ManagedVarArg ||
                            CallingConvention == CallingConvention.NativeC)
                        {
                            if (sentinelIndex > 0)
                                throw new PEException("multiple sentinels in VARARG/C signature");
                            sentinelIndex = i;
                            i--;
                        }
                        else
                            throw new PEException("unexpected sentinel in non-VARARG/C signature");
                    }
                    else
                        parameters.Add(param);
                }
                Parameters = parameters;
            }
            else
                Parameters = Constants.EmptyTypeWithCustomMods;
            VarArgs = sentinelIndex < 0 ? 0 : paramCount - sentinelIndex;
        }

        public override MemberSigTag Tag
        {
            get
            {
                var res = default(MemberSigTag);
                if (!IsStatic)
                {
                    res |= MemberSigTag.HASTHIS;
                    if (IsExplicitThis)
                        res |= MemberSigTag.EXPLICITTHIS;
                }
                if (TypeArity > 0)
                    res |= MemberSigTag.GENERIC;
                switch (CallingConvention)
                {
                    case CallingConvention.Managed:
                        break;
                    case CallingConvention.ManagedVarArg:
                        res |= MemberSigTag.VARARG;
                        break;
                    case CallingConvention.NativeC:
                        res |= MemberSigTag.C;
                        break;
                    case CallingConvention.NativeStd:
                        res |= MemberSigTag.STDCALL;
                        break;
                    case CallingConvention.NativeThis:
                        res |= MemberSigTag.THISCALL;
                        break;
                    case CallingConvention.NativeFast:
                        res |= MemberSigTag.FASTCALL;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                return res;
            }
            set
            {

                IsStatic = (value & MemberSigTag.HASTHIS) == 0;
                IsExplicitThis = IsStatic ? false : (value & MemberSigTag.EXPLICITTHIS) != 0;
                TypeArity = 0;
                if ((value & MemberSigTag.VARARG) != 0)
                    CallingConvention = CallingConvention.ManagedVarArg;
                else if ((value & MemberSigTag.C) != 0)
                    CallingConvention = CallingConvention.NativeC;
                else if ((value & MemberSigTag.STDCALL) != 0)
                    CallingConvention = CallingConvention.NativeStd;
                else if ((value & MemberSigTag.THISCALL) != 0)
                    CallingConvention = CallingConvention.NativeThis;
                else if ((value & MemberSigTag.FASTCALL) != 0)
                    CallingConvention = CallingConvention.NativeFast;
                else if ((value & MemberSigTag.GENERIC) != 0)
                {
                    CallingConvention = CallingConvention.Managed;
                    TypeArity = 1; // Actual arity must be read
                }
                else
                    CallingConvention = CallingConvention.Managed;
            }
        }

        public override MemberSigFlavor Flavor { get { return MemberSigFlavor.Method; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            ReturnType.ResolveIndexes(ctxt);
            foreach (var parameter in Parameters)
                parameter.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            ReturnType.PersistIndexes(ctxt);
            foreach (var parameter in Parameters)
                parameter.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            if (TypeArity > 0)
                writer.WriteCompressedUInt32((uint)TypeArity);
            writer.WriteCompressedUInt32((uint)Parameters.Count);
            ReturnType.Write(ctxt, writer);
            for (var i = 0; i < Parameters.Count; i++)
            {
                if (i == Parameters.Count - VarArgs)
                    new SentinelPsuedoTypeSig().Write(ctxt, writer);
                Parameters[i].Write(ctxt, writer);
            }
        }

        public bool IsMethodRef
        {
            get { return CallingConvention == CallingConvention.Managed; }
        }
    }

    // S23.2.15
    public class MethodSpecMemberSig : MemberSig
    {
        public IImSeq<TypeSig> Arguments;

        public static MethodSpecMemberSig ReadMethodSpec(ReaderContext ctxt, BlobReader reader)
        {
            var tag = (MemberSigTag)reader.ReadByte();
            var res = new MethodSpecMemberSig() { Tag = tag };
            res.ReadRest(ctxt, reader);
            return res;
        }


        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            var count = (int)reader.ReadCompressedUInt32();
            var arguments = new Seq<TypeSig>(count);
            for (var i = 0; i < count; i++)
                arguments.Add(TypeSig.Read(ctxt, reader));
            Arguments = arguments;
        }

        public override MemberSigTag Tag
        {
            get { return MemberSigTag.GENERICINST; }
            set
            {
                if ((value & MemberSigTag.MASK) != MemberSigTag.GENERICINST)
                    throw new PEException("invalid method spec blob");
            }
        }

        public override MemberSigFlavor Flavor { get { return MemberSigFlavor.MethodSpec; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            foreach (var argument in Arguments)
                argument.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            foreach (var argument in Arguments)
                argument.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            writer.WriteCompressedUInt32((uint)Arguments.Count);
            foreach (var argument in Arguments)
                argument.Write(ctxt, writer);
        }
    }

    // S23.2.4
    public class FieldMemberSig : MemberSig
    {
        public TypeWithCustomMods Type;

        public static FieldMemberSig ReadField(ReaderContext ctxt, BlobReader reader)
        {
            var tag = (MemberSigTag)reader.ReadByte();
            var res = new FieldMemberSig { Tag = tag };
            res.ReadRest(ctxt, reader);
            return res;
        }


        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            Type = new TypeWithCustomMods();
            Type.Read(ctxt, reader);
        }

        public override MemberSigTag Tag
        {
            get { return MemberSigTag.FIELD; }
            set
            {
                if ((value & MemberSigTag.MASK) != MemberSigTag.FIELD)
                    throw new PEException("invalid field blob");
            }
        }

        public override MemberSigFlavor Flavor { get { return MemberSigFlavor.Field; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            Type.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            Type.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            Type.Write(ctxt, writer);
        }
    }

    // S23.2.5
    public class PropertyMemberSig : MemberSig
    {
        public bool IsStatic;
        public TypeWithCustomMods ReturnType;
        public IImSeq<TypeSig> Parameters;

        public static PropertyMemberSig ReadProperty(ReaderContext ctxt, BlobReader reader)
        {
            var tag = (MemberSigTag)reader.ReadByte();
            var res = new PropertyMemberSig { Tag = tag };
            res.ReadRest(ctxt, reader);
            return res;
        }

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            var paramCount = (int)reader.ReadCompressedUInt32();
            ReturnType = new TypeWithCustomMods();
            ReturnType.Read(ctxt, reader);
            if (paramCount > 0)
            {
                var parameters = new Seq<TypeSig>(paramCount);
                for (var i = 0; i < paramCount; i++)
                    parameters.Add(TypeSig.Read(ctxt, reader));
                Parameters = parameters;
            }
            else
                Parameters = Constants.EmptyTypeSigs;
        }

        public override MemberSigTag Tag
        {
            get
            {
                var res = MemberSigTag.PROPERTY;
                if (!IsStatic)
                    res |= MemberSigTag.HASTHIS;
                return res;
            }
            set {
                if ((value & MemberSigTag.MASK) != MemberSigTag.PROPERTY)
                    throw new PEException("invalid property blob");
                IsStatic = (value & MemberSigTag.HASTHIS) == 0;
            }
        }

        public override MemberSigFlavor  Flavor { get { return MemberSigFlavor.Property; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            ReturnType.ResolveIndexes(ctxt);
            foreach (var parameter in Parameters)
                parameter.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            ReturnType.PersistIndexes(ctxt);
            foreach (var parameter in Parameters)
                parameter.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            writer.WriteCompressedUInt32((uint)Parameters.Count);
            ReturnType.Write(ctxt, writer);
            foreach (var parameter in Parameters)
                parameter.Write(ctxt, writer);
        }
    }

    // Factored out of  S23.2.6
    public class LocalVar
    {
        public bool IsPinned;
        public TypeWithCustomMods Type;

        public void Read(ReaderContext ctxt, BlobReader reader)
        {
            var customMods = default(Seq<CustomModPseudoTypeSig>);
            var type = TypeSig.Read(ctxt, reader);
            while (type.IsCustomMod || type.IsPinned)
            {
                if (type.IsCustomMod)
                {
                    if (customMods == null)
                        customMods = new Seq<CustomModPseudoTypeSig>();
                    customMods.Add((CustomModPseudoTypeSig)type);
                }
                else
                    IsPinned = true;
                type = TypeSig.Read(ctxt, reader);
            }
            Type = new TypeWithCustomMods { CustomMods = customMods ?? Constants.EmptyCustomModSigs, Type = type };
        }

        public void ResolveIndexes(ReaderContext ctxt)
        {
            Type.ResolveIndexes(ctxt);
        }

        public void PersistIndexes(WriterContext ctxt)
        {
            Type.PersistIndexes(ctxt);
        }

        public void Write(WriterContext ctxt, BlobWriter writer)
        {
            foreach (var type in Type.CustomMods)
                type.Write(ctxt, writer);
            if (IsPinned)
                new PinnedPsuedoTypeSig().Write(ctxt, writer);
            Type.Write(ctxt, writer);
        }
    }

    // S23.2.6
    public class LocalVarMemberSig : MemberSig
    {
        public IImSeq<LocalVar> Variables;

        public static LocalVarMemberSig ReadLocalVar(ReaderContext ctxt, BlobReader reader)
        {
            var tag = (MemberSigTag)reader.ReadByte();
            var res = new LocalVarMemberSig { Tag = tag };
            res.ReadRest(ctxt, reader);
            return res;
        }

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            var variableCount = (int)reader.ReadCompressedUInt32();
            if (variableCount > 0)
            {
                var variables = new Seq<LocalVar>(variableCount);
                for (var i = 0; i < variableCount; i++)
                {
                    var l = new LocalVar();
                    l.Read(ctxt, reader);
                    variables.Add(l);
                }
                Variables = variables;
            }
            else
                Variables = Constants.EmptyLocalVars;
        }

        public override MemberSigTag Tag
        {
            get { return MemberSigTag.LOCAL_SIG; }
            set
            {
                if ((value & MemberSigTag.MASK) != MemberSigTag.LOCAL_SIG)
                    throw new PEException("invalid local var signature");
            }
        }

        public override MemberSigFlavor Flavor { get { return MemberSigFlavor.LocalVar; } }

        public override void ResolveIndexes(ReaderContext ctxt)
        {
            foreach (var variable in Variables)
                variable.ResolveIndexes(ctxt);
        }

        public override void PersistIndexes(WriterContext ctxt)
        {
            foreach (var variable in Variables)
                variable.PersistIndexes(ctxt);
        }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            base.Write(ctxt, writer);
            writer.WriteCompressedUInt32((uint)Variables.Count);
            foreach (var variable in Variables)
                variable.Write(ctxt, writer);
        }
    }

    // ----------------------------------------------------------------------
    // Custom attribute property types
    // ----------------------------------------------------------------------

    public enum CustomAttributePropertyFlavor
    {
        Primitive,
        Enum,
        Object,
        Array
    }

    public abstract class CustomAttributePropertyType
    {
        public static CustomAttributePropertyType Read(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
            var tag = (TypeSigTag)reader.ReadByte();
            var res = default(CustomAttributePropertyType);
            switch (tag)
            {
            case TypeSigTag.SZARRAY:
                res = new ArrayCustomAttributePropertyType();
                break;
            case TypeSigTag.CUSTOM_ATTRIBUTE_BOXED_ARGUMENT:
                res = new ObjectCustomAttributePropertyType();
                break;
            case TypeSigTag.CUSTOM_ATTRIBUTE_ENUM:
                {
                    var typeName = reader.ReadUTF8SizedString();
                    res = resolveType(typeName);
                    if (!(res is EnumCustomAttributePropertyType))
                        throw new PEException("invalid enumeration type in custom attribute");
                    break;
                }
            default:
                res = new PrimitiveCustomAttributePropertyType { Type = PrimitiveTypeSig.FromTag(tag) };
                break;
            }
            res.ReadRest(reader, resolveType);
            return res;
        }

        public static CustomAttributePropertyType FromRuntimeType(Type type)
        {
            if (type.IsArray)
                return new ArrayCustomAttributePropertyType { ElementType = FromRuntimeType(type.GetElementType()) };
            else
            {
                switch (type.FullName)
                {
                case "System.Boolean":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Boolean };
                case "System.Char":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Char };
                case "System.SByte":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Int8 };
                case "System.Int16":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Int16 };
                case "System.Int32":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Int32 };
                case "System.Int64":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Int64 };
                case "System.IntPtr":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.IntNative };
                case "System.Byte":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.UInt8 };
                case "System.UInt16":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.UInt16 };
                case "System.UInt32":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.UInt32 };
                case "System.UInt64":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.UInt64 };
                case "System.UIntPtr":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.UIntNative };
                case "System.Single":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Single };
                case "System.Double":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Double };
                case "System.String":
                    return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.String };
                default:
                    throw new PEException("unrecognised runtime type");
                }
            }
        }

        public static CustomAttributePropertyType FromObject(object obj)
        {
            if (obj == null)
                throw new PEException("boxed value cannot be null within custom attribute property");
            var enumVal = obj as EnumCustomAttributePropertyValue;
            if (enumVal != null)
                return enumVal.Type;
            var typeVal = obj as TypeCustomAttributePropertyValue;
            if (typeVal != null)
                return new PrimitiveCustomAttributePropertyType { Type = PrimitiveType.Type };
            return FromRuntimeType(obj.GetType());
        }

        public abstract CustomAttributePropertyFlavor Flavor { get; }

        public abstract void ReadRest(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType);
        public abstract void Write(BlobWriter writer);
        public abstract object ReadValue(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType);
        public abstract Array NewArray(int size);
        public abstract void WriteValue(BlobWriter writer, object value);
    }

    public class PrimitiveCustomAttributePropertyType : CustomAttributePropertyType
    {
        public PrimitiveType Type;

        public override void ReadRest(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
        }

        public override CustomAttributePropertyFlavor Flavor { get { return CustomAttributePropertyFlavor.Primitive; } }

        public override object ReadValue(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
            switch (Type)
            {
            case PrimitiveType.Boolean:
                return reader.ReadByte() == 0 ? false : true;
            case PrimitiveType.Char:
                return (char)reader.ReadUInt16();
            case PrimitiveType.Int8:
                return reader.ReadSByte();
            case PrimitiveType.Int16:
                return reader.ReadInt16();
            case PrimitiveType.Int32:
                return reader.ReadInt32();
            case PrimitiveType.Int64:
                return reader.ReadInt64();
            case PrimitiveType.UInt8:
                return reader.ReadByte();
            case PrimitiveType.UInt16:
                return reader.ReadUInt16();
            case PrimitiveType.UInt32:
                return reader.ReadUInt32();
            case PrimitiveType.UInt64:
                return reader.ReadUInt64();
            case PrimitiveType.IntNative:
            case PrimitiveType.UIntNative:
                throw new PEException("cannot read native integers");
            case PrimitiveType.Single:
                return reader.ReadSingle();
            case PrimitiveType.Double:
                return reader.ReadDouble();
            case PrimitiveType.String:
                return reader.ReadUTF8SizedString();
            case PrimitiveType.Type:
                return new TypeCustomAttributePropertyValue { Name = reader.ReadUTF8SizedString() };
            case PrimitiveType.Object:
            case PrimitiveType.TypedRef:
            case PrimitiveType.Void:
                throw new PEException("invalid type tag in custom attribute");
            default:
                throw new ArgumentOutOfRangeException();
            }
        }

        public override Array NewArray(int size)
        {
            switch (Type)
            {
            case PrimitiveType.Boolean:
                return new bool[size];
            case PrimitiveType.Char:
                return new char[size];
            case PrimitiveType.Int8:
                return new sbyte[size];
            case PrimitiveType.Int16:
                return new short[size];
            case PrimitiveType.Int32:
                return new int[size];
            case PrimitiveType.Int64:
                return new long[size];
            case PrimitiveType.IntNative:
                return new IntPtr[size];
            case PrimitiveType.UInt8:
                return new byte[size];
            case PrimitiveType.UInt16:
                return new ushort[size];
            case PrimitiveType.UInt32:
                return new uint[size];
            case PrimitiveType.UInt64:
                return new ulong[size];
            case PrimitiveType.UIntNative:
                return new UIntPtr[size];
            case PrimitiveType.Single:
                return new float[size];
            case PrimitiveType.Double:
                return new double[size];
            case PrimitiveType.String:
                return new string[size];
            case PrimitiveType.Type:
                return new TypeCustomAttributePropertyValue[size];
            case PrimitiveType.Object:
            case PrimitiveType.TypedRef:
            case PrimitiveType.Void:
                throw new PEException("invalid type tag in custom attribute");
            default:
                throw new ArgumentOutOfRangeException("type");
            }
        }

        public override void  Write(BlobWriter writer)
        {
            writer.WriteByte((byte)PrimitiveTypeSig.ToTag(Type));
        }

        public override void  WriteValue(BlobWriter writer, object value)
        {
            switch (Type)
            {
            case PrimitiveType.Boolean:
                {
                    var b = (bool)value;
                    writer.WriteByte(b ? (byte)1 : (byte)0);
                }
                break;
            case PrimitiveType.Char:
                {
                    var c = (char)value;
                    writer.WriteUInt16(c);
                }
                break;
            case PrimitiveType.Int8:
                {
                    var i = (sbyte)value;
                    writer.WriteSByte(i);
                }
                break;
            case PrimitiveType.Int16:
                {
                    var i = (short)value;
                    writer.WriteInt16(i);
                }
                break;
            case PrimitiveType.Int32:
                {
                    var i = (int)value;
                    writer.WriteInt32(i);
                }
                break;
            case PrimitiveType.Int64:
                {
                    var i = (long)value;
                    writer.WriteInt64(i);
                }
                break;
            case PrimitiveType.UInt8:
                {
                    var i = (byte)value;
                    writer.WriteByte(i);
                }
                break;
            case PrimitiveType.UInt16:
                {
                    var i = (ushort)value;
                    writer.WriteUInt16(i);
                }
                break;
            case PrimitiveType.UInt32:
                {
                    var i = (uint)value;
                    writer.WriteUInt32(i);
                }
                break;
            case PrimitiveType.UInt64:
                {
                    var i = (ulong)value;
                    writer.WriteUInt64(i);
                }
                break;
            case PrimitiveType.IntNative:
            case PrimitiveType.UIntNative:
                throw new InvalidOperationException("cannot write native integers");
            case PrimitiveType.Single:
                {
                    var f = (float)value;
                    writer.WriteSingle(f);
                }
                break;
            case PrimitiveType.Double:
                {
                    var d = (double)value;
                    writer.WriteDouble(d);
                }
                break;
            case PrimitiveType.String:
                {
                    var s = (string)value;
                    writer.WriteUTF8SizedString(s);
                }
                break;
            case PrimitiveType.Type:
                {
                    var t = (TypeCustomAttributePropertyValue)value;
                    writer.WriteUTF8SizedString(t.Name);
                }
                break;
            case PrimitiveType.Object:
            case PrimitiveType.TypedRef:
            case PrimitiveType.Void:
                throw new PEException("invalid type tag in custom attribute");
            default:
                throw new ArgumentOutOfRangeException();
            }
        }
    }

    public class EnumCustomAttributePropertyType : CustomAttributePropertyType
    {
        // Name either of form <qualified type name> or <qualified type name>, <strong assembly name>
        public string TypeName;
        public CustomAttributePropertyType UnderlyingType;

        public override void ReadRest(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
        }

        public override CustomAttributePropertyFlavor Flavor { get { return CustomAttributePropertyFlavor.Enum; } }

        public override object ReadValue(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
            var val = UnderlyingType.ReadValue(reader, resolveType);
            return new EnumCustomAttributePropertyValue { Type = this, Value = val };
        }

        public override Array NewArray(int size)
        {
            return UnderlyingType.NewArray(size);
        }

        public override void  Write(BlobWriter writer)
        {
            writer.WriteByte((byte)TypeSigTag.CUSTOM_ATTRIBUTE_ENUM);
            writer.WriteUTF8SizedString(TypeName);
        }
        
        public override void  WriteValue(BlobWriter writer, object value)
        {
            var enumVal = value as EnumCustomAttributePropertyValue;
            if (enumVal == null)
                throw new PEException("object is not an enumeration value");
            UnderlyingType.WriteValue(writer, enumVal.Value);
        }
    }

    public class ObjectCustomAttributePropertyType : CustomAttributePropertyType
    {
        public override void ReadRest(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
        }

        public override CustomAttributePropertyFlavor Flavor { get { return CustomAttributePropertyFlavor.Object; } }

        public override object ReadValue(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
            return Read(reader, resolveType).ReadValue(reader, resolveType);
        }

        public override Array NewArray(int size)
        {
            return new object[size];
        }

        public override void  Write(BlobWriter writer)
        {
            writer.WriteByte((byte)TypeSigTag.CUSTOM_ATTRIBUTE_BOXED_ARGUMENT);
        }

        public override void  WriteValue(BlobWriter writer, object value)
        {
            if (value == null)
                throw new PEException("boxed value cannot be null within custom attribute property");
            var type = FromObject(value);
            type.Write(writer);
            type.WriteValue(writer, value);
        }
    }

    public class ArrayCustomAttributePropertyType : CustomAttributePropertyType
    {
        public CustomAttributePropertyType ElementType;

        public override void ReadRest(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
            ElementType = Read(reader, resolveType);
        }

        public override CustomAttributePropertyFlavor Flavor { get { return CustomAttributePropertyFlavor.Array; } }

        public override object ReadValue(BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
            var size = reader.ReadInt32();
            var arr = ElementType.NewArray(size);
            for (var i = 0; i < size; i++)
                arr.SetValue(ElementType.ReadValue(reader, resolveType), i);
            return arr;
        }

        public override Array NewArray(int size)
        {
            throw new PEException("custom attribute properties cannot be array of arrays");
        }

        public override void Write(BlobWriter writer)
        {
            writer.WriteByte((byte)TypeSigTag.SZARRAY);
            ElementType.Write(writer);
        }

        public override void WriteValue(BlobWriter writer, object value)
        {
            var arr = (Array)value;
            var size = arr.Length;
            writer.WriteInt32(size);
            for (var i = 0; i < size; i++)
                ElementType.WriteValue(writer, arr.GetValue(i));
        }
    }

    // ----------------------------------------------------------------------
    // Property values
    // ----------------------------------------------------------------------

    // We can't represent type references yet, so capture type as it's fully qualified type name
    public class TypeCustomAttributePropertyValue
    {
        // Name either of form <qualified type name> or <qualified type name>, <strong assembly name>
        public string Name;
    }

    // We can't box enum values, so wrap them explicity
    public class EnumCustomAttributePropertyValue
    {
        public EnumCustomAttributePropertyType Type;
        public object Value;
    }

    // ----------------------------------------------------------------------
    // Custom attributes
    // ----------------------------------------------------------------------

    public class CustomAttributeProperty
    {
        public CustomAttributePropertyType Type;
        public object Value;
    }

    public class CustomAttributeSignature : Signature
    {
        public IImSeq<CustomAttributeProperty> FixedArgs;
        public IImMap<string, CustomAttributeProperty> FieldArgs;
        public IImMap<string, CustomAttributeProperty> PropertyArgs;

        private const ushort prolog = 0x0001;

        public override void ReadRest(ReaderContext ctxt, BlobReader reader)
        {
            throw new InvalidOperationException("not supported for custom attribute signatures");
        }

        public void Read(IImSeq<CustomAttributePropertyType> fixedArgTypes, BlobReader reader, Func<string, CustomAttributePropertyType> resolveType)
        {
            var fieldArgs = default(Map<string, CustomAttributeProperty>);
            var propertyArgs = default(Map<string, CustomAttributeProperty>);
            if (reader.AtEndOfBlob)
            {
                if (fixedArgTypes.Count > 0)
                    throw new PEException("expected fixed arguments in custom attribute");
                FixedArgs = Constants.EmptyCustomAttributeProperties;
                FieldArgs = Constants.EmptyNamedCustomAttributueProperties;
                PropertyArgs = Constants.EmptyNamedCustomAttributueProperties;
            }
            else
            {
                if (reader.ReadUInt16() != prolog)
                    throw new PEException("invalid custom attribute");
                if (fixedArgTypes.Count > 0)
                {
                    var fixedArgs = new Seq<CustomAttributeProperty>(fixedArgTypes.Count);
                    for (var i = 0; i < fixedArgTypes.Count; i++)
                    {
                        var type = fixedArgTypes[i];
                        fixedArgs.Add
                            (new CustomAttributeProperty { Type = type, Value = type.ReadValue(reader, resolveType) });
                    }
                    FixedArgs = fixedArgs;
                }
                else
                    FixedArgs = Constants.EmptyCustomAttributeProperties;
                var numNamed = reader.ReadUInt16();
                for (var i = 0; i < numNamed; i++)
                {
                    var tag = (TypeSigTag)reader.ReadByte();
                    var type = CustomAttributePropertyType.Read(reader, resolveType);
                    var nm = reader.ReadUTF8SizedString();
                    var prop = new CustomAttributeProperty { Type = type, Value = type.ReadValue(reader, resolveType) };
                    if (tag == TypeSigTag.CUSTOM_ATTRIBUTE_FIELD)
                    {
                        if (fieldArgs == null)
                            fieldArgs = new Map<string, CustomAttributeProperty>();
                        if (fieldArgs.ContainsKey(nm))
                            throw new PEException("duplicate named field in custom attribute");
                        fieldArgs.Add(nm, prop);
                    }
                    else if (tag == TypeSigTag.CUSTOM_ATTRIBUTE_PROPERTY)
                    {
                        if (propertyArgs == null)
                            propertyArgs = new Map<string, CustomAttributeProperty>();
                        if (propertyArgs.ContainsKey(nm))
                            throw new PEException("duplicate named property in custom attribute");
                        propertyArgs.Add(nm, prop);
                    }
                    else
                        throw new PEException("invalid custom attribute");
                }
                FieldArgs = fieldArgs ?? Constants.EmptyNamedCustomAttributueProperties;
                PropertyArgs = propertyArgs ?? Constants.EmptyNamedCustomAttributueProperties;
            }
        }

        public override void ResolveIndexes(ReaderContext ctxt) { }

        public override void PersistIndexes(WriterContext ctxt) { }

        public override void Write(WriterContext ctxt, BlobWriter writer)
        {
            if (FixedArgs.Count > 0 || FieldArgs.Count > 0 || PropertyArgs.Count > 0)
            {
                writer.WriteUInt16(prolog);

                foreach (var p in FixedArgs)
                    p.Type.WriteValue(writer, p.Value);

                writer.WriteUInt16((ushort)(FieldArgs.Count + PropertyArgs.Count));

                foreach (var kv in FieldArgs)
                {
                    writer.WriteByte((byte)TypeSigTag.CUSTOM_ATTRIBUTE_FIELD);
                    kv.Value.Type.Write(writer);
                    writer.WriteUTF8SizedString(kv.Key);
                    kv.Value.Type.WriteValue(writer, kv.Value.Value);
                }
                foreach (var kv in PropertyArgs)
                {
                    writer.WriteByte((byte)TypeSigTag.CUSTOM_ATTRIBUTE_PROPERTY);
                    kv.Value.Type.Write(writer);
                    writer.WriteUTF8SizedString(kv.Key);
                    kv.Value.Type.WriteValue(writer, kv.Value.Value);
                }
            }
        }
    }
}
