﻿using BankAppointmentScheduler.Domain;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BankAppointmentScheduler.BankSchedulerService.Requests;
using BankAppointmentScheduler.BankSchedulerService.ResponseModels;
using BankAppointmentScheduler.BankSchedulerService.ViewModels;
using BankAppointmentScheduler.Common.Exceptions;
using BankAppointmentScheduler.Common.Extensions;
using BankAppointmentScheduler.Domain.BankEntities.Entities;
using BankAppointmentScheduler.Domain.BankEntities.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankAppointmentScheduler.BankSchedulerService
{
    public class BankAppointmentSchedulerService : IBankAppointmentSchedulerService
    {
        private readonly IBankAppointmentContext _context;

        public BankAppointmentSchedulerService(IBankAppointmentContext context)
        {
            _context = context;
        }

        public async Task ScheduleAppointment(CreateScheduleModel model, CancellationToken cancellationToken)
        {
            model.ArrivalTime ??= model.ArrivalDate.TimeOfDay;
            var entity = model.Cast();
            await ValidateAppointment(entity);

            await _context.Appointments.AddAsync(entity, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAppointment(UpdateScheduleModel model, CancellationToken cancellationToken = default)
        {
            model.ArrivalTime ??= model.ArrivalDate.TimeOfDay;
            var entity = await _context.Appointments.FindAsync(model.UserId, model.BranchId, model.ServiceId);
            if (entity == null)
                throw new NotFoundException(nameof(Appointment), new { model.UserId, model.BranchId, model.ServiceId });

            var mappedEntity = model.Map(entity);
            await ValidateAppointment(entity);

            _context.Appointments.Update(mappedEntity);

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task ChangeAppointmentStatus(UpdateAppointmentStatus model, CancellationToken cancellationToken = default)
        {
            var entity = await _context.Appointments.FindAsync(model.UserId, model.BranchId, model.ServiceId);
            if (entity == null)
                throw new NotFoundException(nameof(Appointment), new { model.UserId, model.BranchId, model.ServiceId });

            var mappedEntity = model.Map(entity);

            _context.Appointments.Update(mappedEntity);

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task BulkChangeAppointmentStatus(BulkUpdateAppointmentStatus request, CancellationToken cancellationToken = default)
        {
            if (!(request.Keys?.Any() ?? false))
                throw new NotFoundException(nameof(Appointment), request.Keys);

            var usersToSearch = request.Keys.Select(x => x.UserId).ToList();
            var branchesToSearch = request.Keys.Select(x => x.BranchId).ToList();
            var servicesToSearch = request.Keys.Select(x => x.ServiceId).ToList();

            var newStatus = request.AppointmentStatus.ToString();

            var entities = (await _context.Appointments
                    .Where(x => usersToSearch.Contains(x.UserId) &&
                                branchesToSearch.Contains(x.BranchId) &&
                                servicesToSearch.Contains(x.ServiceId) &&
                                x.Status != newStatus)
                    .ToListAsync(cancellationToken))
                .Where(x => request.Keys
                    .Any(key => key.UserId == x.UserId &&
                                key.BranchId == x.BranchId &&
                                key.ServiceId == x.ServiceId)).ToList();
            if (!entities.Any()) return;

            foreach (var entity in entities)
            {
                request.Map(entity);
            }

            _context.Appointments.UpdateRange(entities);

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<AppointmentStatus>> GetAppointmentStatuses(CancellationToken cancellationToken = default)
        {
            return await Task.Run(EnumExtensions.GetList<AppointmentStatus>, cancellationToken);
        }

        public async Task<AppointmentListViewModel> GetPaginatedAppointments(GetPaginatedAppointments request, CancellationToken cancellationToken = default)
        {
            var entities = await _context.Appointments
                .Where(x => (!request.UserIds.Any() || request.UserIds.Contains(x.UserId)) &&
                            (!request.ServiceIds.Any() || request.ServiceIds.Contains(x.ServiceId)) &&
                            (!request.BranchIds.Any() || request.BranchIds.Contains(x.BranchId)))
                .Select(AppointmentViewModel.QueryableProjection)
                .OrderBy(x => x.ArrivalDate)
                .Skip((request.Page - 1) * request.Take)
                .Take(request.Take)
                .ToListAsync(cancellationToken);

            var totalNumberOfEntities = await _context.Appointments
                .CountAsync(x => (!request.UserIds.Any() || request.UserIds.Contains(x.UserId)) &&
                                 (!request.ServiceIds.Any() || request.ServiceIds.Contains(x.ServiceId)) &&
                                 (!request.BranchIds.Any() || request.BranchIds.Contains(x.BranchId)),
                    cancellationToken: cancellationToken);

            return new AppointmentListViewModel
            {
                Appointments = entities,
                TotalNumberOfElements = totalNumberOfEntities
            };
        }

        public async Task<AppointmentDetailsModel> GetAppointmentDetails(GetAppointmentDetailsQuery query, CancellationToken cancellationToken = default)
        {
            var appointment = await _context.Appointments
                .Where(x => x.UserId == query.UserId &&
                            x.BranchId == query.BranchId &&
                            x.ServiceId == query.ServiceId)
                .Select(AppointmentDetailsModel.AsQueryableProjection)
                .FirstOrDefaultAsync(cancellationToken);

            return appointment;
        }

        public async Task<BranchAppointmentListViewModel> GetBranchAppointments(GetBranchAppointmentsQuery query, CancellationToken cancellationToken = default)
        {
            var branch = await _context.Branches
                .Where(x => x.BranchId == query.BranchId)
                .Select(BranchAppointmentListViewModel.AsQueryableProjection)
                .FirstOrDefaultAsync(cancellationToken);

            var appointments = await _context.Appointments
                .Where(x => x.BranchId == query.BranchId && x.ArrivalDate.Date == query.SearchDate)
                .Select(BranchAppointmentViewModel.AsQueryableProjection)
                .ToListAsync(cancellationToken);

            branch.Appointments = appointments;

            return branch;
        }

        private async Task ValidateAppointment(Appointment entity)
        {
            var isAnyOpenCounter = await ValidateAppointmentCounters(entity);
            var isAnyOpenBranch = await ValidateAppointmentBranch(entity);

            if (!(isAnyOpenCounter && isAnyOpenBranch))
                throw new AppointmentTimeInvalidException(isAnyOpenBranch, isAnyOpenBranch);
        }

        private async Task<bool> ValidateAppointmentCounters(Appointment entity)
        {
            var countersOccupiedIds = _context.Counters
                .Where(c => c.BranchId == entity.BranchId && c.CounterServices
                    .Any(cs => cs.ServiceId == entity.ServiceId && cs.Service.Appointments
                        .Any(ap => ap.UserId != entity.UserId && ap.ArrivalDate == entity.ArrivalDate &&
                                   ap.ArrivalTime == entity.ArrivalTime)))
                .Select(x => x.CounterId);
                

            return await _context.Counters.AnyAsync(x => x.BranchId == entity.BranchId && 
                                                         x.CounterServices.Any(cs => cs.ServiceId == entity.ServiceId) &&
                                                         !countersOccupiedIds.Contains(x.CounterId));
        }

        private async Task<bool> ValidateAppointmentBranch(Appointment entity)
        {
            var weekday = entity.ArrivalDate.DayOfWeek.Cast();

            return await _context.Branches.AnyAsync(x => x.BranchId == entity.BranchId && 
                                                         x.Schedules.Any(s => s.WeekDay == weekday && 
                                                                              s.OpeningTime <= entity.ArrivalTime && 
                                                                              entity.ArrivalTime < s.ClosingTime));
        }
    }
}
