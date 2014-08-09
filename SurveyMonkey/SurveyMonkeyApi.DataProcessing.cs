﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace SurveyMonkey
{
    public partial class SurveyMonkeyApi
    {
        #region Fill all missing survey information

        public void FillMissingSurveyInformation(List<Survey> surveys)
        {
            foreach (var survey in surveys)
            {
                FillMissingSurveyInformation(survey);
            }
        }

        public void FillMissingSurveyInformation(Survey survey)
        {
            try
            {
                FillMissingCollectorDetails(survey);
                FillMissingResponseCounts(survey);
                FillMissingSurveyDetails(survey);
                FillMissingResponseDetails(survey);
                MatchResponsesToSurveyStructure(survey);
            }
            catch (Exception e)
            {
                throw new SurveyMonkeyException("Could not fill missing survey information", e);
            }
        }

        #endregion

        #region Fill missing survey details

        private void FillMissingSurveyDetails(Survey survey)
        {
            Survey surveyDetails = GetSurveyDetails(survey.SurveyId);
            survey.DateCreated = surveyDetails.DateCreated;
            survey.DateModified = surveyDetails.DateModified;
            survey.Language = surveyDetails.Language;
            survey.NumResponses = surveyDetails.NumResponses;
            survey.QuestionCount = surveyDetails.QuestionCount;
            survey.Nickname = surveyDetails.Nickname;
            survey.TitleText = surveyDetails.TitleText;
            survey.TitleEnabled = surveyDetails.TitleEnabled;
            survey.Pages = surveyDetails.Pages;
        }

        #endregion

        #region Fill missing collector details

        private void FillMissingCollectorDetails(Survey survey)
        {
            survey.Collectors = GetCollectorList(survey.SurveyId);
        }

        #endregion

        #region Fill missing response details

        private void FillMissingResponseDetails(Survey survey)
        {
            List<Response> responses = GetAllSurveyResponses(survey);

            //Need to initialise responses before adding to them
            foreach (var collector in survey.Collectors)
            {
                collector.Responses = new List<Response>();
            }
            
            Dictionary<long, Collector> collectorLookup = survey.Collectors.ToDictionary(c => c.CollectorId, c => c);
            foreach (var response in responses)
            {
                collectorLookup[response.Respondent.CollectorId].Responses.Add(response);
            }

            survey.Collectors = collectorLookup.Values.ToList();
        }

        private List<Response> GetAllSurveyResponses(Survey survey)
        {
            const int maxRespondentsPerPage = 100;
            List<Respondent> respondents = GetRespondentList(survey.SurveyId);
            Dictionary<long, Respondent> respondentLookup = respondents.ToDictionary(r => r.RespondentId, r => r);
            var responses = new List<Response>();

            //page through the respondents
            bool moreRespondents = true;
            int page = 0;
            while (moreRespondents)
            {
                List<long> respondentIds = respondents.Skip(page * maxRespondentsPerPage).Take(maxRespondentsPerPage).Select(rp => rp.RespondentId).ToList();
                if (respondentIds.Count > 0)
                {
                    List<Response> newResponses = GetResponses(survey.SurveyId, respondentIds);

                    foreach (var newResponse in newResponses)
                    {
                        newResponse.Respondent = respondentLookup[newResponse.RespondentId];
                    }
                    responses.AddRange(newResponses);
                }
                if (respondentIds.Count < 100)
                {
                    moreRespondents = false;
                }

                page++;
            }
            return responses;
        }

        #endregion

        #region Fill missing response counts

        private void FillMissingResponseCounts(Survey survey)
        {
            foreach (var collector in survey.Collectors)
            {
                Collector countDetails = GetResponseCounts(collector.CollectorId);
                collector.Completed = countDetails.Completed;
                collector.Started = countDetails.Started;
            }
        }

        #endregion

        #region Match answers to questions

        private void MatchResponsesToSurveyStructure(Survey survey)
        {
            foreach (var question in survey.Questions)
            {
                question.AnswersLookup = question.Answers.ToDictionary(a => a.AnswerId, a => a);
            }
            Dictionary<long, Question> questionsLookup = survey.Questions.ToDictionary(q => q.QuestionId, q => q);
            foreach (var collector in survey.Collectors)
            {
                MatchCollectorsToSurveyStructure(questionsLookup, collector);
            }
        }

        private void MatchCollectorsToSurveyStructure(Dictionary<long, Question> questionsLookup, Collector collector)
        {
            foreach (var response in collector.Responses)
            {
                MatchIndividualResponseToSurveyStructure(questionsLookup, response);
            }
        }

        private void MatchIndividualResponseToSurveyStructure(Dictionary<long, Question> questionsLookup, Response response)
        {
            foreach (var responseQuestion in response.Questions)
            {
                responseQuestion.ProcessedAnswer = new ProcessedAnswer
                {
                    QuestionFamily = questionsLookup[responseQuestion.QuestionId].Type.Family,
                    QuestionSubtype = questionsLookup[responseQuestion.QuestionId].Type.Subtype,
                    Response = MatchResponseQuestionToSurveyStructure(questionsLookup[responseQuestion.QuestionId], responseQuestion.Answers)
                };                
            }
        }

        private object MatchResponseQuestionToSurveyStructure(Question question, List<ResponseAnswer> responseAnswers)
        {
            switch (question.Type.Family)
            {
                case QuestionFamily.SingleChoice:
                    return MatchSingleChoiceAnswer(question, responseAnswers);

                case QuestionFamily.MultipleChoice:
                    return MatchMultipleChoiceAnswer(question, responseAnswers);

                case QuestionFamily.OpenEnded:
                    switch (question.Type.Subtype)
                    {
                        case QuestionSubtype.Essay:
                        case QuestionSubtype.Single:
                            return MatchOpenEndedSingleAnswer(question, responseAnswers);

                        case QuestionSubtype.Multi:
                        case QuestionSubtype.Numerical:
                            return MatchOpenEndedMultipleAnswer(question, responseAnswers);
                    }
                    break;

                case QuestionFamily.Demographic:
                    return MatchDemographicAnswer(question, responseAnswers);

                case QuestionFamily.DateTime:
                    return MatchDateTimeAnswer(question, responseAnswers);

                case QuestionFamily.Matrix:
                    switch (question.Type.Subtype)
                    {
                        case QuestionSubtype.Menu:
                            return MatchMatrixMenuAnswer(question, responseAnswers);
                        case QuestionSubtype.Ranking:
                            return MatchMatrixRankingAnswer(question, responseAnswers);
                        case QuestionSubtype.Rating:
                            return MatchMatrixRatingAnswer(question, responseAnswers);
                        case QuestionSubtype.Single:
                            return MatchMatrixSingleAnswer(question, responseAnswers);
                        case QuestionSubtype.Multi:
                            return MatchMatrixMultiAnswer(question, responseAnswers);
                    }
                    break;
            }
            return null;
        }

        private SingleChoiceAnswer MatchSingleChoiceAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new SingleChoiceAnswer();
            
            foreach (var responseAnswer in responseAnswers)
            {
                if (question.AnswersLookup[responseAnswer.Row].Type == AnswerType.Row)
                {
                    reply.Choice = question.AnswersLookup[responseAnswer.Row].Text;
                }
                if (question.AnswersLookup[responseAnswer.Row].Type == AnswerType.Other)
                {
                    reply.OtherText = responseAnswer.Text;
                    if (reply.Choice == null)
                    {
                        reply.Choice = question.AnswersLookup[responseAnswer.Row].Text;
                    }
                }
            }
            return reply;
        }

        private MultipleChoiceAnswer MatchMultipleChoiceAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new MultipleChoiceAnswer
            {
                Choices = new List<string>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                if (question.AnswersLookup[responseAnswer.Row].Type == AnswerType.Row)
                {
                    reply.Choices.Add(question.AnswersLookup[responseAnswer.Row].Text);
                }
                if (question.AnswersLookup[responseAnswer.Row].Type == AnswerType.Other)
                {
                    reply.Choices.Add(question.AnswersLookup[responseAnswer.Row].Text);
                    reply.OtherText = responseAnswer.Text;
                }
            }
            return reply;
        }

        private OpenEndedSingleAnswer MatchOpenEndedSingleAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new OpenEndedSingleAnswer();

            reply.Text = responseAnswers.First().Text;

            return reply;
        }

        private OpenEndedMultipleAnswer MatchOpenEndedMultipleAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new OpenEndedMultipleAnswer
            {
                Rows = new List<OpenEndedMultipleAnswerRow>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                var openEndedMultipleAnswerReply = new OpenEndedMultipleAnswerRow();
                openEndedMultipleAnswerReply.RowName = question.AnswersLookup[responseAnswer.Row].Text;
                openEndedMultipleAnswerReply.Text = responseAnswer.Text;
                reply.Rows.Add(openEndedMultipleAnswerReply);
            }

            return reply;
        }

        private DemographicAnswer MatchDemographicAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new DemographicAnswer();

            foreach (var responseAnswer in responseAnswers)
            {
                var propertyName = question.AnswersLookup[responseAnswer.Row].Type.ToString();
                typeof(DemographicAnswer).GetProperty(propertyName, (BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance)).SetValue(reply, responseAnswer.Text);
            }
            return reply;
        }

        private DateTimeAnswer MatchDateTimeAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new DateTimeAnswer
            {
                Rows = new List<DateTimeAnswerRow>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                var dateTimeAnswerReply = new DateTimeAnswerRow();
                dateTimeAnswerReply.RowName = question.AnswersLookup[responseAnswer.Row].Text;
                dateTimeAnswerReply.TimeStamp = DateTime.MinValue;

                DateTime timeStamp = DateTime.Parse(responseAnswer.Text, CultureInfo.CreateSpecificCulture("en-US"));
                if (question.Type.Subtype == QuestionSubtype.TimeOnly) //Where only a time is given, use date component from DateTime.MinValue
                {
                    dateTimeAnswerReply.TimeStamp = dateTimeAnswerReply.TimeStamp.AddHours(timeStamp.Hour);
                    dateTimeAnswerReply.TimeStamp = dateTimeAnswerReply.TimeStamp.AddMinutes(timeStamp.Minute);
                }
                else
                {
                    dateTimeAnswerReply.TimeStamp = timeStamp;
                }

                reply.Rows.Add(dateTimeAnswerReply);
            }
            return reply;
        }

        private MatrixMenuAnswer MatchMatrixMenuAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixMenuAnswer
            {
                Rows = new Dictionary<long, MatrixMenuAnswerRow>()
            };

            Dictionary<long, string> choicesLookup = (from answerItem in question.AnswersLookup where answerItem.Value.Items != null from item in answerItem.Value.Items select item).ToDictionary(item => item.AnswerId, item => item.Text);

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.Row == 0)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else
                {
                    if (!reply.Rows.ContainsKey(responseAnswer.Row))
                    {
                        reply.Rows.Add(responseAnswer.Row, new MatrixMenuAnswerRow
                        {
                            Columns = new Dictionary<long, MatrixMenuAnswerColumn>()
                        });
                    }
                    if (!reply.Rows[responseAnswer.Row].Columns.ContainsKey(responseAnswer.Col))
                    {
                        reply.Rows[responseAnswer.Row].Columns.Add(responseAnswer.Col, new MatrixMenuAnswerColumn());
                    }

                    reply.Rows[responseAnswer.Row].RowName = question.AnswersLookup[responseAnswer.Row].Text;
                    reply.Rows[responseAnswer.Row].Columns[responseAnswer.Col].ColumnName = question.AnswersLookup[responseAnswer.Col].Text;
                    reply.Rows[responseAnswer.Row].Columns[responseAnswer.Col].Choice = choicesLookup[responseAnswer.ColChoice];
                }   
            }

            return reply;
        }

        private MatrixRankingAnswer MatchMatrixRankingAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixRankingAnswer
            {
                Ranking = new Dictionary<int, string>(),
                NotApplicable = new List<string>()
            };
            
            foreach (var responseAnswer in responseAnswers)
            {
                if (question.AnswersLookup[responseAnswer.Col].Weight == 0)
                {
                    reply.NotApplicable.Add(question.AnswersLookup[responseAnswer.Row].Text);
                }
                else
                {
                    reply.Ranking.Add(question.AnswersLookup[responseAnswer.Col].Weight, question.AnswersLookup[responseAnswer.Row].Text);
                }
            }
            return reply;
        }

        private MatrixRatingAnswer MatchMatrixRatingAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixRatingAnswer
            {
                Rows = new List<MatrixRatingAnswerRow>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.Row == 0)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else
                {
                    var row = new MatrixRatingAnswerRow();
                    row.RowName = question.AnswersLookup[responseAnswer.Row].Text;

                    if (responseAnswer.Col != 0)
                    {
                        row.Choice = question.AnswersLookup[responseAnswer.Col].Text;
                    }
                    
                    if (!String.IsNullOrEmpty(responseAnswer.Text))
                    {
                        row.OtherText = responseAnswer.Text;
                    }
                    reply.Rows.Add(row);
                }
            }

            return reply;
        }

        private MatrixSingleAnswer MatchMatrixSingleAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixSingleAnswer
            {
                Rows = new List<MatrixSingleAnswerRow>()
            };

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.Row == 0)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else
                {
                    reply.Rows.Add(new MatrixSingleAnswerRow
                    {
                        RowName = question.AnswersLookup[responseAnswer.Row].Text,
                        Choice = question.AnswersLookup[responseAnswer.Col].Text
                    });
                }
            }

            return reply;
        }

        private MatrixMultiAnswer MatchMatrixMultiAnswer(Question question, List<ResponseAnswer> responseAnswers)
        {
            var reply = new MatrixMultiAnswer();

            var rows = new Dictionary<long, MatrixMultiAnswerRow>();

            foreach (var responseAnswer in responseAnswers)
            {
                if (responseAnswer.Row == 0)
                {
                    reply.OtherText = responseAnswer.Text;
                }
                else
                {
                    if (!rows.ContainsKey(responseAnswer.Row))
                    {
                        rows.Add(responseAnswer.Row, new MatrixMultiAnswerRow
                        {
                            RowName = question.AnswersLookup[responseAnswer.Row].Text,
                            Choices = new List<string>()
                        });
                    }
                    rows[responseAnswer.Row].Choices.Add(question.AnswersLookup[responseAnswer.Col].Text);
                }
            }

            reply.Rows = rows.Values.ToList();

            return reply;
        }

        #endregion
    }
}