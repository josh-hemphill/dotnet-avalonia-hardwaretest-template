namespace HardwareTest.Authoring;

/// MATLAB-flavored subset AST. Not MATLAB Runtime.
public sealed record FormulaAst(FormulaExpr Root);

public abstract record FormulaExpr;

public sealed record NumberExpr(double Value) : FormulaExpr;

public sealed record IdentExpr(string Name) : FormulaExpr;

public sealed record UnaryExpr(string Op, FormulaExpr Operand) : FormulaExpr;

public sealed record BinaryExpr(string Op, FormulaExpr Left, FormulaExpr Right) : FormulaExpr;

public sealed record CallExpr(string Name, IReadOnlyList<FormulaExpr> Args) : FormulaExpr;
