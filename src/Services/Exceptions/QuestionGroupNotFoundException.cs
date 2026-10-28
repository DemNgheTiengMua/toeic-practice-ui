using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed class QuestionGroupNotFoundException(int groupId)
    : AdminExamException(ApiError.Codes.QuestionGroupNotFound, $"Question group with id {groupId} not found.", 404);
