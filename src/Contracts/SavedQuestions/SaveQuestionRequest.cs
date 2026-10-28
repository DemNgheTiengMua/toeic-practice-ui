using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record SaveQuestionRequest(string? Source = "manual");
